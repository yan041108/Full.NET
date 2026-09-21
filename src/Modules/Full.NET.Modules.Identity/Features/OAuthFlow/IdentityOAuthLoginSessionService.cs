using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.OAuthFlow;

/// <summary>为已绑定 OAuth 外部身份签发本地会话，不执行密码校验。</summary>
internal sealed class IdentityOAuthLoginSessionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    IPermissionSnapshotReader permissionSnapshotReader,
    IAccessTokenIssuer accessTokenIssuer,
    IRandomTokenGenerator randomTokenGenerator,
    IOptions<IdentityOptions> options,
    IdentitySessionRealtimeDelivery sessionRealtimeDelivery)
{
    private readonly IdentityOptions _options = options.Value;

    /// <summary>为已激活且已绑定的用户签发刷新/访问/CSRF 令牌。</summary>
    /// <param name="userId">本地用户标识。</param>
    /// <param name="normalizedUsername">审计用规范化用户名。</param>
    /// <param name="client">客户端请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录会话结果或稳定业务错误。</returns>
    public async Task<Result<Features.Login.LoginSessionResult>> IssueSessionAsync(
        Guid userId,
        string normalizedUsername,
        ClientRequestContext client,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return InvalidCredentials();
        }

        var user = ToUser(record);
        if (user.LockoutEndUtc > clock.UtcNow)
        {
            return InvalidCredentials();
        }

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
        var session = new Domain.RefreshSession(
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
        await IdentitySessionLoginPolicyCoordinator.EnforceAfterSuccessfulLoginAsync(
                _options.SessionLoginPolicy,
                user.Id,
                sessionId,
                _options.ClientId,
                queryExecutor,
                commandExecutor,
                clock,
                sessionRealtimeDelivery,
                cancellationToken)
            .ConfigureAwait(false);

        await WriteAuditAsync(
            user.Id,
            sessionId,
            normalizedUsername,
            cancellationToken).ConfigureAwait(false);

        var accessToken = accessTokenIssuer.Issue(
            user,
            sessionId,
            null,
            authorization.Permissions,
            authorization.IsSuperAdministrator);
        return Result<Features.Login.LoginSessionResult>.Success(
            new Features.Login.LoginSessionResult(
                new TokenResponse(
                    accessToken.AccessToken,
                    "Bearer",
                    accessToken.ExpiresAtUtc),
                refreshToken,
                csrfToken));
    }

    private async Task WriteAuditAsync(
        Guid userId,
        Guid sessionId,
        string normalizedUsername,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            userId,
            sessionId,
            TokenHash.Compute(normalizedUsername),
            "login",
            "identity.oauth_login_succeeded",
            true,
            null,
            null,
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
        if (affectedRows is not (1 or -1))
        {
            throw new InvalidOperationException(
                $"Identity {operation} affected {affectedRows} rows instead of one.");
        }
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

    private static Result<Features.Login.LoginSessionResult> InvalidCredentials() =>
        Result<Features.Login.LoginSessionResult>.Failure(new Error(
            Code: IdentityErrorCodes.InvalidCredentials,
            Message: "The OAuth account cannot sign in.",
            Type: ErrorType.Unauthorized));
}
