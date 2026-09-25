using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
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
    ICommandTransaction transaction,
    AuthenticationSecurityEventWriter authenticationEvents,
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
                IdentitySqlParameters.Create(("UserId", userId)),
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
                IdentitySqlParameters.Create(("UserId", userId)),
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
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        var parameters = IdentitySqlParameters.Create(
            ("UserId", userId),
            ("SecretProtected", secretProtector.Protect(sharedSecret)),
            ("CreatedAtUtc", now),
            ("UpdatedAtUtc", now));
        await transaction.ExecuteAsync(async token =>
        {
            var affected = await commandExecutor.ExecuteAsync(
                    existing is null ? IdentitySql.InsertUserTotpPending
                        : IdentitySql.ResetUserTotpPending,
                    parameters, token)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                throw new InvalidOperationException("TOTP enrollment state was not written.");
            }

            await authenticationEvents.WriteAsync(userId, userId,
                "mfa.totp_enrollment_started", "identity.mfa_totp_enrollment_started",
                true, "totp", token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

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
            await authenticationEvents.WriteAsync(userId, null,
                "mfa.totp_enrollment_confirmed", IdentityErrorCodes.MfaTotpRequired,
                false, "totp", cancellationToken).ConfigureAwait(false);
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpRequired,
                "A TOTP code is required to confirm enrollment.",
                ErrorType.Validation));
        }

        var pending = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserTotpRecord>(
                IdentitySql.FindUserTotpByUserId,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (pending is null || string.IsNullOrEmpty(pending.SecretProtected))
        {
            await authenticationEvents.WriteAsync(userId, null,
                "mfa.totp_enrollment_confirmed", IdentityErrorCodes.MfaTotpNotEnrolled,
                false, "totp", cancellationToken).ConfigureAwait(false);
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
            await authenticationEvents.WriteAsync(userId, null,
                "mfa.totp_enrollment_confirmed", IdentityErrorCodes.MfaTotpInvalid,
                false, "totp", cancellationToken).ConfigureAwait(false);
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
            await authenticationEvents.WriteAsync(userId, null,
                "mfa.totp_enrollment_confirmed", IdentityErrorCodes.MfaTotpInvalid,
                false, "totp", cancellationToken).ConfigureAwait(false);
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The pending TOTP credential is malformed.",
                ErrorType.Validation));
        }

        if (!TotpAlgorithm.Verify(key, totpCode, clock.UtcNow))
        {
            await authenticationEvents.WriteAsync(userId, null,
                "mfa.totp_enrollment_confirmed", IdentityErrorCodes.MfaTotpInvalid,
                false, "totp", cancellationToken).ConfigureAwait(false);
            return Result<TotpEnrollmentStatusResponse>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The TOTP code is invalid.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var affected = await transaction.ExecuteAsync(async token =>
        {
            var rows = await commandExecutor.ExecuteAsync(
                    IdentitySql.ConfirmUserTotp,
                    IdentitySqlParameters.Create(
                        ("UserId", userId),
                        ("ConfirmedAtUtc", now),
                        ("UpdatedAtUtc", now),
                        ("Version", pending.Version)),
                    token)
                .ConfigureAwait(false);
            if (rows == 1)
            {
                await authenticationEvents.WriteAsync(userId, userId,
                    "mfa.totp_enrollment_confirmed", "identity.mfa_totp_enrollment_confirmed",
                    true, "totp", token).ConfigureAwait(false);
            }
            return rows;
        }, cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            await authenticationEvents.WriteAsync(userId, null,
                "mfa.totp_enrollment_confirmed", IdentityErrorCodes.MfaTotpInvalid,
                false, "totp", cancellationToken).ConfigureAwait(false);
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
