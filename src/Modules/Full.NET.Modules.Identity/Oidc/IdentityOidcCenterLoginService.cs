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

/// <summary>内部认证结果；仅用于安全审计，对外仍返回统一的凭据拒绝。</summary>
internal sealed record IdentityOidcCenterLoginAttempt(
    IdentityOidcCenterLoginResult? Login,
    Guid? UserId,
    string ResultCode);

/// <summary>中心登录凭据校验；不创建旧 Refresh Session，仅返回账号权威状态。</summary>
internal sealed class IdentityOidcCenterLoginService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IOptions<Full.NET.Modules.Identity.Configuration.IdentityOptions> identityOptions)
{
    private const string HostScope = "host";
    private static readonly (IdentityUser User, string PasswordHash) TimingDefenseCredential =
        CreateTimingDefenseCredential();
    private readonly Full.NET.Modules.Identity.Configuration.IdentityOptions _identityOptions = identityOptions.Value;

    public async Task<IdentityOidcCenterLoginResult?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
        => (await AuthenticateWithOutcomeAsync(username, password, cancellationToken)
            .ConfigureAwait(false)).Login;

    public async Task<IdentityOidcCenterLoginAttempt> AuthenticateWithOutcomeAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.Trim().ToUpperInvariant();
        // 与旧登录入口保持有界乐观重试；每次冲突后重读账号并重新验证密码。
        for (var attempt = 0; attempt < 32; attempt++)
        {
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
                return new IdentityOidcCenterLoginAttempt(null, null, "identity.user-not-found");
            }

            var user = ToUser(record);
            if (!user.IsActive)
            {
                return new IdentityOidcCenterLoginAttempt(null, user.Id, "identity.user-disabled");
            }
            if (user.LockoutEndUtc > clock.UtcNow)
            {
                return new IdentityOidcCenterLoginAttempt(null, user.Id, "identity.user-locked");
            }

            var verification = passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                password);
            if (verification == PasswordVerificationResult.Failed)
            {
                var failedLoginCount = user.FailedLoginCount + 1;
                var lockoutEnd = failedLoginCount >= _identityOptions.LockoutThreshold
                    ? clock.UtcNow.AddMinutes(_identityOptions.LockoutMinutes)
                    : (DateTimeOffset?)null;
                var failureRows = await commandExecutor.ExecuteAsync(
                        IdentitySql.UpdateLoginFailure,
                        new LoginFailureUpdate(
                            user.Id,
                            failedLoginCount,
                            lockoutEnd,
                            clock.UtcNow,
                            user.Version),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (failureRows == 0)
                {
                    continue;
                }
                return new IdentityOidcCenterLoginAttempt(null, user.Id, "identity.invalid-password");
            }

            var passwordHash = verification == PasswordVerificationResult.SuccessRehashNeeded
                ? passwordHasher.HashPassword(user, password)
                : user.PasswordHash;
            var successRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.UpdateLoginSuccess,
                    new LoginSuccessUpdate(user.Id, passwordHash, clock.UtcNow, user.Version),
                    cancellationToken)
                .ConfigureAwait(false);
            if (successRows == 0)
            {
                continue;
            }
            if (successRows != 1)
            {
                return new IdentityOidcCenterLoginAttempt(null, user.Id, "identity.login-update-failed");
            }

            return new IdentityOidcCenterLoginAttempt(
                new IdentityOidcCenterLoginResult(
                    user.Id, user.Username, user.DisplayName, user.SecurityStamp),
                user.Id,
                "identity.oidc_center_login_succeeded");
        }

        // 持续争用时拒绝创建中心会话，不能使用未写入的旧账号状态登录。
        return new IdentityOidcCenterLoginAttempt(null, null, "identity.login-contention");
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
