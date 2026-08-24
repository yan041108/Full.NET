using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;
using Full.NET.Modules.Identity.Authorization;
using global::Dapper;

namespace Full.NET.Modules.Identity.Features.Login;

/// <summary>
/// Host 作用域用户名密码登录处理器。事务/并发顺序：
/// 1) 未知账号仍执行一次 PBKDF2 哈希校验（Timing Defense）防用户名枚举；
/// 2) RecordFailureAsync / RecordSuccessAsync 使用乐观并发 + 最多 32 次重试推进 Version，
///    冲突后重新读取最新用户快照并重新验证密码，避免并发改密/停用被旧会话绕过；
/// 3) 成功后写入 RefreshSession、写入 AuthAuditEvent、签发 Access Token；
/// 4) CSRF Token 与 Refresh Token 同步生成，由 Endpoint 层写 Cookie。
/// </summary>
internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator,
    IPermissionSnapshotReader permissionSnapshotReader,
    IAccessTokenIssuer accessTokenIssuer,
    IRandomTokenGenerator randomTokenGenerator,
    IOptions<IdentityOptions> options)
    : ICommandHandler<Command, LoginSessionResult>
{
    private const string HostScope = "host";
    private const int MaxLoginUpdateAttempts = 32;
    private static readonly (IdentityUser User, string PasswordHash)
        TimingDefenseCredential = CreateTimingDefenseCredential();
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<LoginSessionResult>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = command.Username.Trim().ToUpperInvariant();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                CreateFindUserParameters(normalizedUsername),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            // 未知账号仍执行一次真实 PBKDF2 校验，缩小用户名枚举的响应时间差。
            _ = passwordHasher.VerifyHashedPassword(
                TimingDefenseCredential.User,
                TimingDefenseCredential.PasswordHash,
                command.Password);
            await WriteAuditAsync(
                null,
                null,
                normalizedUsername,
                "login",
                "identity.user-not-found",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        var user = ToUser(record);
        if (!user.IsActive)
        {
            await WriteAuditAsync(
                user.Id,
                null,
                normalizedUsername,
                "login",
                "identity.user-disabled",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        if (user.LockoutEndUtc > clock.UtcNow)
        {
            await WriteAuditAsync(
                user.Id,
                null,
                normalizedUsername,
                "login",
                "identity.account-locked",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            command.Password);
        if (verification == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(user, command, cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        var successfulUser = await RecordSuccessAsync(
            user,
            command.Password,
            verification,
            cancellationToken).ConfigureAwait(false);
        if (successfulUser is null)
        {
            await WriteAuditAsync(
                user.Id,
                null,
                normalizedUsername,
                "login",
                "identity.login-state-changed",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        user = successfulUser;
        var authorization = await permissionSnapshotReader.ReadAsync(
                user.Id,
                user.ScopeKey,
                user.TenantId,
                cancellationToken)
            .ConfigureAwait(false);
        var sessionId = idGenerator.NewId();
        var familyId = idGenerator.NewId();
        var refreshToken = randomTokenGenerator.Generate(32);
        var csrfToken = randomTokenGenerator.Generate(32);
        var session = new Full.NET.Modules.Identity.Domain.RefreshSession(
            sessionId,
            user.Id,
            familyId,
            _options.ClientId,
            TokenHash.Compute(refreshToken),
            clock.UtcNow.AddDays(_options.RefreshTokenDays),
            null,
            null,
            null,
            null,
            clock.UtcNow,
            1);
        await EnsureSingleRowAsync(
            IdentitySql.InsertRefreshSession,
            session,
            "refresh session insert",
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            user.Id,
            sessionId,
            normalizedUsername,
            "login",
            "identity.login_succeeded",
            true,
            command,
            cancellationToken).ConfigureAwait(false);

        var accessToken = accessTokenIssuer.Issue(
            user,
            sessionId,
            null,
            authorization.Permissions,
            authorization.IsSuperAdministrator);
        return Result<LoginSessionResult>.Success(new LoginSessionResult(
            new TokenResponse(
                accessToken.AccessToken,
                "Bearer",
                accessToken.ExpiresAtUtc),
            refreshToken,
            csrfToken));
    }

    private async Task RecordFailureAsync(
        IdentityUser user,
        Command command,
        CancellationToken cancellationToken)
    {
        var current = user;
        for (var attempt = 0; attempt < MaxLoginUpdateAttempts; attempt++)
        {
            if (current.LockoutEndUtc > clock.UtcNow)
            {
                await WriteAuditAsync(
                    current.Id,
                    null,
                    current.NormalizedUsername,
                    "login",
                    "identity.account-locked",
                    false,
                    command,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var failedLoginCount = current.FailedLoginCount + 1;
            var lockoutEnd = failedLoginCount >= _options.LockoutThreshold
                ? clock.UtcNow.AddMinutes(_options.LockoutMinutes)
                : (DateTimeOffset?)null;
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.UpdateLoginFailure,
                    new LoginFailureUpdate(
                        current.Id,
                        failedLoginCount,
                        lockoutEnd,
                        clock.UtcNow,
                        current.Version),
                    cancellationToken)
                .ConfigureAwait(false);
            if (IsSingleRowAffected(affectedRows))
            {
                await WriteAuditAsync(
                    current.Id,
                    null,
                    current.NormalizedUsername,
                    "login",
                    lockoutEnd.HasValue
                        ? "identity.account-locked"
                        : "identity.password-mismatch",
                    false,
                    command,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            // 失败计数使用乐观并发；冲突后重读可避免高并发错误登录泄漏为 500。
            var refreshed = await queryExecutor
                .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindUserByScopeAndUsername,
                    CreateFindUserParameters(current.NormalizedUsername),
                    cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is null)
            {
                break;
            }

            current = ToUser(refreshed);
        }

        throw new InvalidOperationException(
            "Identity login failure update exceeded the bounded concurrency retry limit.");
    }

    private async Task<IdentityUser?> RecordSuccessAsync(
        IdentityUser user,
        string password,
        Microsoft.AspNetCore.Identity.PasswordVerificationResult initialVerification,
        CancellationToken cancellationToken)
    {
        var current = user;
        var verification = initialVerification;
        for (var attempt = 0; attempt < MaxLoginUpdateAttempts; attempt++)
        {
            if (!current.IsActive || current.LockoutEndUtc > clock.UtcNow)
            {
                return null;
            }

            if (attempt > 0)
            {
                verification = passwordHasher.VerifyHashedPassword(
                    current,
                    current.PasswordHash,
                    password);
                if (verification
                    == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
                {
                    return null;
                }
            }

            var passwordHash = verification
                == Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded
                ? passwordHasher.HashPassword(current, password)
                : current.PasswordHash;
            var updatedAt = clock.UtcNow;
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.UpdateLoginSuccess,
                    new LoginSuccessUpdate(
                        current.Id,
                        passwordHash,
                        updatedAt,
                        current.Version),
                    cancellationToken)
                .ConfigureAwait(false);
            if (IsSingleRowAffected(affectedRows))
            {
                return current with
                {
                    PasswordHash = passwordHash,
                    FailedLoginCount = 0,
                    LockoutEndUtc = null,
                    UpdatedAtUtc = updatedAt,
                    Version = current.Version + 1,
                };
            }

            // 冲突后重新验证最新密码哈希，避免并发改密或停用被旧快照绕过。
            var refreshed = await queryExecutor
                .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindUserByScopeAndUsername,
                    CreateFindUserParameters(current.NormalizedUsername),
                    cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is null)
            {
                return null;
            }

            current = ToUser(refreshed);
        }

        throw new InvalidOperationException(
            "Identity login success update exceeded the bounded concurrency retry limit.");
    }

    private async Task WriteAuditAsync(
        Guid? userId,
        Guid? sessionId,
        string normalizedUsername,
        string eventType,
        string resultCode,
        bool succeeded,
        Command command,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            userId,
            sessionId,
            TokenHash.Compute(normalizedUsername),
            eventType,
            resultCode,
            succeeded,
            Truncate(command.Client.IpAddress, 64),
            Truncate(command.Client.UserAgent, 512),
            null,
            clock.UtcNow);
        await EnsureSingleRowAsync(
            IdentitySql.InsertAuthAudit,
            audit,
            "authentication audit insert",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSingleRowAsync(
        SqlStatement statement,
        object parameters,
        string operation,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsSingleRowAffected(affectedRows))
        {
            throw new InvalidOperationException(
                $"Identity {operation} affected {affectedRows} rows instead of one.");
        }
    }

    /// <summary>
    /// 与 <see cref="ICommandExecutor.ExecuteAsync"/> 约定一致：部分提供程序在 NOCOUNT 等场景返回 -1。
    /// </summary>
    private static bool IsSingleRowAffected(int affectedRows) => affectedRows is 1 or -1;

    private static DynamicParameters CreateFindUserParameters(string normalizedUsername)
    {
        var parameters = new DynamicParameters();
        parameters.Add("ScopeKey", HostScope);
        parameters.Add("NormalizedUsername", normalizedUsername);
        return parameters;
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

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static Result<LoginSessionResult> InvalidCredentials() =>
        Result<LoginSessionResult>.Failure(new Error(
            Code: IdentityErrorCodes.InvalidCredentials,
            Message: "The username or password is invalid.",
            Type: ErrorType.Unauthorized));

    private static (IdentityUser User, string PasswordHash)
        CreateTimingDefenseCredential()
    {
        var user = new IdentityUser(
            Guid.Empty,
            null,
            HostScope,
            "timing-defense",
            "TIMING-DEFENSE",
            "Timing Defense",
            string.Empty,
            false,
            0,
            null,
            string.Empty,
            DateTimeOffset.UnixEpoch,
            null,
            1);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = new Microsoft.AspNetCore.Identity.PasswordHasher<IdentityUser>()
            .HashPassword(user, password);
        return (user, hash);
    }
}
