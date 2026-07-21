using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// Production 合格强认证：当前密码与已启用 TOTP 同时成立。
/// </summary>
internal sealed class TotpStrongReauthenticationProvider(
    IQueryExecutor queryExecutor,
    IPasswordHasher<IdentityUser> passwordHasher,
    TotpSecretProtector secretProtector,
    IClock clock) : IStrongReauthenticationProvider
{
    public bool IsProductionEligible => true;

    public async Task<Result<IdentityUser>> VerifyAsync(
        Guid operatorUserId,
        string currentPassword,
        string? totpCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            return ReauthenticationFailed();
        }

        if (string.IsNullOrWhiteSpace(totpCode))
        {
            return Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.MfaTotpRequired,
                "A TOTP code is required for production strong reauthentication.",
                ErrorType.Unauthorized));
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = operatorUserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is not { IsActive: true })
        {
            return ReauthenticationFailed();
        }

        var user = ToUser(record);
        var passwordResult = passwordHasher.VerifyHashedPassword(
            user,
            record.PasswordHash,
            currentPassword);
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            return ReauthenticationFailed();
        }

        var totp = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserTotpRecord>(
                IdentitySql.FindUserTotpByUserId,
                new { UserId = operatorUserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (totp is not { IsEnabled: true }
            || string.IsNullOrEmpty(totp.SecretProtected))
        {
            return Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.MfaTotpNotEnrolled,
                "The operator has not enrolled TOTP.",
                ErrorType.Forbidden));
        }

        string sharedSecret;
        try
        {
            sharedSecret = secretProtector.Unprotect(totp.SecretProtected);
        }
        catch
        {
            return Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The TOTP credential cannot be read.",
                ErrorType.Unauthorized));
        }

        byte[] key;
        try
        {
            key = TotpAlgorithm.DecodeSharedSecret(sharedSecret);
        }
        catch (FormatException)
        {
            return Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The TOTP credential is malformed.",
                ErrorType.Unauthorized));
        }

        if (!TotpAlgorithm.Verify(key, totpCode, clock.UtcNow))
        {
            return Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.MfaTotpInvalid,
                "The TOTP code is invalid.",
                ErrorType.Unauthorized));
        }

        return Result<IdentityUser>.Success(user);
    }

    private static IdentityUser ToUser(IdentityUserRecord record) => new(
        record.Id,
        record.TenantId,
        record.ScopeKey,
        record.Username,
        record.NormalizedUsername,
        record.DisplayName,
        record.PasswordHash,
        record.IsActive,
        record.FailedLoginCount,
        record.LockoutEndUtc,
        record.SecurityStamp,
        record.CreatedAtUtc,
        record.UpdatedAtUtc,
        record.Version,
        record.PreferredLocale,
        record.ProfileVersion);

    private static Result<IdentityUser> ReauthenticationFailed() =>
        Result<IdentityUser>.Failure(new Error(
            IdentityErrorCodes.SuperAdministratorReauthenticationFailed,
            "The current password reauthentication failed.",
            ErrorType.Unauthorized));
}
