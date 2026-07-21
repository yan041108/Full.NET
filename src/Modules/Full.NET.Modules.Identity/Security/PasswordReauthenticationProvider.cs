using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// Development/Testing 使用的密码重认证；不具备 Production 资格。
/// </summary>
internal sealed class PasswordReauthenticationProvider(
    IQueryExecutor queryExecutor,
    IPasswordHasher<IdentityUser> passwordHasher) : IStrongReauthenticationProvider
{
    public bool IsProductionEligible => false;

    public async Task<Result<IdentityUser>> VerifyAsync(
        Guid operatorUserId,
        string currentPassword,
        string? totpCode,
        CancellationToken cancellationToken = default)
    {
        _ = totpCode;
        if (string.IsNullOrEmpty(currentPassword))
        {
            return ReauthenticationFailed();
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
        var verification = passwordHasher.VerifyHashedPassword(
            user,
            record.PasswordHash,
            currentPassword);
        return verification == PasswordVerificationResult.Failed
            ? ReauthenticationFailed()
            : Result<IdentityUser>.Success(user);
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
