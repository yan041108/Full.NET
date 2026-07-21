using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using FullNetIdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;

namespace Full.NET.Modules.Identity.Features.ManageTotp;

/// <summary>Host 账号自助 TOTP 登记；确认前密钥仅以受保护密文暂存。</summary>
internal sealed class TotpEnrollmentService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    TotpSecretProtector secretProtector,
    IOptions<FullNetIdentityOptions> identityOptions,
    IClock clock)
{
    private readonly FullNetIdentityOptions _identityOptions = identityOptions.Value;

    public async Task<Result<TotpEnrollmentStatusResponse>> GetStatusAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Unauthorized();
        }

        var totp = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserTotpRecord>(
                IdentitySql.FindUserTotpByUserId,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<TotpEnrollmentStatusResponse>.Success(
            new TotpEnrollmentStatusResponse(
                totp is not null,
                totp is { IsEnabled: true }));
    }

    public async Task<Result<BeginTotpEnrollmentResponse>> BeginAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return BeginUnauthorized();
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (user is not { IsActive: true })
        {
            return BeginUnauthorized();
        }

        var sharedSecret = TotpAlgorithm.GenerateSharedSecretBase32();
        var now = clock.UtcNow;
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserTotpRecord>(
                IdentitySql.FindUserTotpByUserId,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        var parameters = new
        {
            UserId = userId,
            SecretProtected = secretProtector.Protect(sharedSecret),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        if (existing is null)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertUserTotpPending,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.ResetUserTotpPending,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<BeginTotpEnrollmentResponse>.Success(
            new BeginTotpEnrollmentResponse(
                sharedSecret,
                TotpAlgorithm.BuildOtpAuthUri(
                    _identityOptions.Issuer,
                    user.Username,
                    sharedSecret)));
    }

    public async Task<Result<TotpEnrollmentStatusResponse>> ConfirmAsync(
        ClaimsPrincipal principal,
        string totpCode,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(totpCode))
        {
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpRequired,
                "A TOTP code is required to confirm enrollment.",
                ErrorType.Validation));
        }

        var pending = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserTotpRecord>(
                IdentitySql.FindUserTotpByUserId,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (pending is null || string.IsNullOrEmpty(pending.SecretProtected))
        {
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpNotEnrolled,
                "Begin TOTP enrollment before confirming.",
                ErrorType.Validation));
        }

        if (pending.IsEnabled)
        {
            return Result<TotpEnrollmentStatusResponse>.Success(
                new TotpEnrollmentStatusResponse(true, true));
        }

        string sharedSecret;
        try
        {
            sharedSecret = secretProtector.Unprotect(pending.SecretProtected);
        }
        catch
        {
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The pending TOTP credential cannot be read.",
                ErrorType.Validation));
        }

        byte[] key;
        try
        {
            key = TotpAlgorithm.DecodeSharedSecret(sharedSecret);
        }
        catch (FormatException)
        {
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The pending TOTP credential is malformed.",
                ErrorType.Validation));
        }

        if (!TotpAlgorithm.Verify(key, totpCode, clock.UtcNow))
        {
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The TOTP code is invalid.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                IdentitySql.ConfirmUserTotp,
                new
                {
                    UserId = userId,
                    ConfirmedAtUtc = now,
                    UpdatedAtUtc = now,
                    Version = pending.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "TOTP enrollment confirmation conflicted; retry begin.",
                ErrorType.Conflict));
        }

        return Result<TotpEnrollmentStatusResponse>.Success(
            new TotpEnrollmentStatusResponse(true, true));
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out userId);

    private static Result<TotpEnrollmentStatusResponse> Unauthorized() =>
        Result<TotpEnrollmentStatusResponse>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is not active.",
            ErrorType.Unauthorized));

    private static Result<BeginTotpEnrollmentResponse> BeginUnauthorized() =>
        Result<BeginTotpEnrollmentResponse>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is not active.",
            ErrorType.Unauthorized));
}
