using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed record IdentityOidcCenterLoginResult(
    Guid UserId,
    string Username,
    string DisplayName,
    string SecurityStamp);

/// <summary>中心登录凭据校验；不创建旧 Refresh Session，仅返回账号权威状态。</summary>
internal sealed class IdentityOidcCenterLoginService(
    IQueryExecutor queryExecutor,
    IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock)
{
    private const string HostScope = "host";
    private static readonly (IdentityUser User, string PasswordHash) TimingDefenseCredential =
        CreateTimingDefenseCredential();

    public async Task<IdentityOidcCenterLoginResult?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.Trim().ToUpperInvariant();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                IdentitySqlParameters.Create(
                    ("ScopeKey", HostScope),
                    ("NormalizedUsername", normalizedUsername)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            _ = passwordHasher.VerifyHashedPassword(
                TimingDefenseCredential.User,
                TimingDefenseCredential.PasswordHash,
                password);
            return null;
        }

        var user = ToUser(record);
        if (!user.IsActive || user.LockoutEndUtc > clock.UtcNow)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        return new IdentityOidcCenterLoginResult(
            user.Id,
            user.Username,
            user.DisplayName,
            user.SecurityStamp);
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
        record.ProfileVersion,
        record.AccountType,
        record.MustChangePassword,
        record.PasswordChangedAtUtc);

    private static (IdentityUser User, string PasswordHash) CreateTimingDefenseCredential()
    {
        var user = new IdentityUser(
            Guid.Empty,
            null,
            HostScope,
            "timing-defense",
            "TIMING-DEFENSE",
            "timing-defense",
            string.Empty,
            true,
            0,
            null,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            null,
            1,
            "zh-CN",
            0,
            "standard",
            false,
            null);
        var hasher = new PasswordHasher<IdentityUser>();
        return (user, hasher.HashPassword(user, Guid.NewGuid().ToString("N")));
    }
}