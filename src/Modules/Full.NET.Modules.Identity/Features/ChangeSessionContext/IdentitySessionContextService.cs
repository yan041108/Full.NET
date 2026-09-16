using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.ChangeSessionContext;

internal sealed class IdentitySessionContextService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IPermissionSnapshotReader permissionSnapshotReader,
    PermissionClaimEvaluator permissionClaimEvaluator,
    IAccessTokenIssuer accessTokenIssuer,
    IdentityOidcContextAccessTokenIssuer oidcContextAccessTokenIssuer,
    IdentityOidcContextRefreshTokenIssuer oidcContextRefreshTokenIssuer,
    IdentityOidcGrantRevocationService oidcGrantRevocationService,
    IdentityOidcClientConfigResolver clientConfigResolver,
    ICurrentTenantContextWriter tenantContextWriter,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<IdentityOidcOptions> oidcOptions,
    IOptions<IdentityOptions> identityOptions) : IIdentitySessionContextService
{
    private const string HostScope = "host";
    private const string SwitchPermission = "tenancy.tenants.switch";
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;
    private readonly IdentityOptions _identityOptions = identityOptions.Value;

    /// <summary>
    /// 使用当前 Access Token 所代表的会话上下文执行一次乐观并发切换并签发新令牌。
    /// </summary>
    /// <param name="principal">包含会话标识、来源作用域和当前租户上下文的已认证身份。</param>
    /// <param name="tenant">目标租户；传入空值表示返回 Host 上下文。</param>
    /// <param name="cancellationToken">用于取消数据库读写的令牌。</param>
    /// <returns>切换成功时返回新令牌和上下文，状态已变化时返回稳定冲突结果。</returns>
    public async Task<Result<TenantContextTokenResponse>> ChangeAsync(
        ClaimsPrincipal principal,
        VerifiedTenantContext? tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var issuer = principal.FindFirstValue(JwtRegisteredClaimNames.Iss);
        if (_oidcOptions.Enable
            && !string.IsNullOrWhiteSpace(_oidcOptions.Issuer)
            && string.Equals(issuer, _oidcOptions.Issuer, StringComparison.Ordinal))
        {
            return await ChangeOidcAsync(principal, tenant, cancellationToken).ConfigureAwait(false);
        }

        if (!TryReadIdentity(
                principal,
                out var userId,
                out var sessionId,
                out var expectedActiveTenantId)
            || !string.Equals(
                principal.FindFirstValue(IdentityClaimTypes.ActorScope),
                HostScope,
                StringComparison.Ordinal))
        {
            return Failure(
                IdentityErrorCodes.InvalidActorScope,
                "The current identity cannot switch tenant context.",
                ErrorType.Forbidden);
        }

        if (!permissionClaimEvaluator.HasPermission(principal, SwitchPermission))
        {
            return Failure(
                CommonErrorCodes.PermissionDenied,
                "The current identity does not have the required permission.",
                ErrorType.Forbidden);
        }

        var record = await FindSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsOwnedActiveHostSession(record, userId, principal))
        {
            return SessionNotActive();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateRefreshSessionContext,
                new RefreshSessionContextUpdate(
                    sessionId,
                    userId,
                    tenant?.Id,
                    expectedActiveTenantId,
                    record!.SessionVersion),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var current = await FindSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (!IsOwnedActiveHostSession(current, userId, principal))
            {
                return SessionNotActive();
            }

            return Failure(
                IdentityErrorCodes.SessionContextConflict,
                "The session context changed concurrently.",
                ErrorType.Conflict);
        }

        var authorization = await permissionSnapshotReader.ReadAsync(
                record!.UserId,
                record.ScopeKey,
                tenant?.Id ?? record.TenantId,
                cancellationToken)
            .ConfigureAwait(false);
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            record.UserId,
            record.SessionId,
            TokenHash.Compute(record.NormalizedUsername),
            "context-switch",
            "identity.session-context-changed",
            true,
            null,
            null,
            tenant?.Id,
            clock.UtcNow);
        var auditRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertContextAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
        if (auditRows != 1)
        {
            throw new InvalidOperationException(
                $"Identity context audit insert affected {auditRows} rows instead of one.");
        }

        var issued = accessTokenIssuer.Issue(
            ToUser(record),
            record.SessionId,
            tenant?.Id,
            authorization.Permissions,
            authorization.IsSuperAdministrator);
        var context = tenant is null
            ? new TenantContextDescriptor(null, "host", "Host", HostScope)
            : new TenantContextDescriptor(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                $"tenant:{tenant.Id:N}");
        return Result<TenantContextTokenResponse>.Success(
            new TenantContextTokenResponse(
                issued.AccessToken,
                "Bearer",
                issued.ExpiresAtUtc,
                context));
    }

    private async Task<Result<TenantContextTokenResponse>> ChangeOidcAsync(
        ClaimsPrincipal principal,
        VerifiedTenantContext? tenant,
        CancellationToken cancellationToken)
    {
        if (!TryReadOidcIdentity(
                principal,
                out var userId,
                out var applicationSessionId,
                out var centerSessionId,
                out var clientId,
                out var expectedActiveTenantId)
            || !string.Equals(
                principal.FindFirstValue(IdentityClaimTypes.ActorScope),
                HostScope,
                StringComparison.Ordinal))
        {
            return Failure(
                IdentityErrorCodes.InvalidActorScope,
                "The current identity cannot switch tenant context.",
                ErrorType.Forbidden);
        }

        if (!permissionClaimEvaluator.HasPermission(principal, SwitchPermission))
        {
            return Failure(
                CommonErrorCodes.PermissionDenied,
                "The current identity does not have the required permission.",
                ErrorType.Forbidden);
        }

        return await RunInHostScopeAsync(
            async ct =>
            {
                var validation = await FindOidcApplicationSessionAsync(applicationSessionId, ct)
                    .ConfigureAwait(false);
                if (!IsOwnedActiveHostOidcSession(validation, userId, principal))
                {
                    return SessionNotActive();
                }

                var effectiveScope = tenant is null ? HostScope : $"tenant:{tenant.Id:N}";
                var now = clock.UtcNow;
                var affectedRows = await commandExecutor.ExecuteAsync(
                        IdentityOidcSessionSql.UpdateApplicationSessionContext,
                        new OidcApplicationSessionContextUpdate(
                            applicationSessionId,
                            userId,
                            tenant?.Id,
                            effectiveScope,
                            expectedActiveTenantId,
                            validation!.Version,
                            now,
                            now),
                        ct)
                    .ConfigureAwait(false);
                if (affectedRows != 1)
                {
                    var current = await FindOidcApplicationSessionAsync(applicationSessionId, ct)
                        .ConfigureAwait(false);
                    if (!IsOwnedActiveHostOidcSession(current, userId, principal))
                    {
                        return SessionNotActive();
                    }

                    return Failure(
                        IdentityErrorCodes.SessionContextConflict,
                        "The session context changed concurrently.",
                        ErrorType.Conflict);
                }

                var authorization = await permissionSnapshotReader.ReadAsync(
                        userId,
                        HostScope,
                        tenant?.Id,
                        ct)
                    .ConfigureAwait(false);
                var username = principal.FindFirstValue(JwtRegisteredClaimNames.Name)
                    ?? principal.FindFirstValue("preferred_username")
                    ?? userId.ToString("D");
                var audit = new AuthAuditEvent(
                    idGenerator.NewId(),
                    userId,
                    applicationSessionId,
                    TokenHash.Compute(username),
                    "context-switch",
                    "identity.session-context-changed",
                    true,
                    null,
                    null,
                    tenant?.Id,
                    now);
                var auditRows = await commandExecutor.ExecuteAsync(
                        IdentitySql.InsertContextAudit,
                        audit,
                        ct)
                    .ConfigureAwait(false);
                if (auditRows != 1)
                {
                    throw new InvalidOperationException(
                        $"Identity context audit insert affected {auditRows} rows instead of one.");
                }

                var resolvedClient = await clientConfigResolver.ResolveAsync(clientId, ct)
                    .ConfigureAwait(false);
                var audience = string.IsNullOrWhiteSpace(resolvedClient?.ResourceAudience)
                    ? _identityOptions.Audience
                    : resolvedClient!.ResourceAudience!;
                var isExternalClient = string.IsNullOrWhiteSpace(
                    principal.FindFirstValue(IdentityClaimTypes.SecurityStamp));
                var issueRequest = new IdentityOidcContextAccessTokenIssueRequest(
                    userId,
                    principal.FindFirstValue(JwtRegisteredClaimNames.Name) ?? username,
                    principal.FindFirstValue("preferred_username") ?? username,
                    centerSessionId,
                    applicationSessionId,
                    clientId,
                    HostScope,
                    effectiveScope,
                    tenant?.Id,
                    ReadOAuthScopes(principal),
                    authorization.Permissions,
                    authorization.IsSuperAdministrator,
                    validation!.UserSecurityStamp,
                    isExternalClient,
                    audience);
                var issued = oidcContextAccessTokenIssuer.Issue(issueRequest);
                await oidcGrantRevocationService.RevokeRefreshTokensByUserAndClientAsync(
                        userId,
                        clientId,
                        ct)
                    .ConfigureAwait(false);
                var refreshToken = await oidcContextRefreshTokenIssuer.TryIssueAsync(
                        principal,
                        issueRequest,
                        ct)
                    .ConfigureAwait(false);
                var context = tenant is null
                    ? new TenantContextDescriptor(null, "host", "Host", HostScope)
                    : new TenantContextDescriptor(
                        tenant.Id,
                        tenant.Identifier,
                        tenant.Name,
                        effectiveScope);
                return Result<TenantContextTokenResponse>.Success(
                    new TenantContextTokenResponse(
                        issued.AccessToken,
                        "Bearer",
                        issued.ExpiresAtUtc,
                        context,
                        refreshToken));
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunInHostScopeAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var wasHost = tenantContextWriter.IsHost;
        var previousTenant = tenantContextWriter.Id is Guid tenantId
            ? new TenantContext(tenantId, tenantContextWriter.Identifier!, tenantContextWriter.Name!)
            : null;
        tenantContextWriter.SetHost();
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (previousTenant is not null)
            {
                tenantContextWriter.SetTenant(previousTenant);
            }
            else if (wasHost)
            {
                tenantContextWriter.SetHost();
            }
            else
            {
                tenantContextWriter.Clear();
            }
        }
    }

    /// <summary>按会话标识读取刷新会话，用于上下文切换前后的并发校验。</summary>
    /// <param name="sessionId">刷新会话标识。</param>
    /// <param name="cancellationToken">用于取消数据库查询的令牌。</param>
    /// <returns>存在时返回刷新会话记录，否则返回空值。</returns>
    private Task<RefreshSessionRecord?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var parameters = IdentitySqlParameters.Create(("SessionId", sessionId));
        return queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            parameters,
            cancellationToken);
    }

    private bool IsOwnedActiveHostSession(
        RefreshSessionRecord? record,
        Guid userId,
        ClaimsPrincipal principal)
    {
        return record is not null
            && record.UserId == userId
            && string.Equals(record.ScopeKey, HostScope, StringComparison.Ordinal)
            && string.Equals(
                record.SecurityStamp,
                principal.FindFirstValue(IdentityClaimTypes.SecurityStamp),
                StringComparison.Ordinal)
            && record.IsActive
            && record.ExpiresAtUtc > clock.UtcNow
            && !record.ConsumedAtUtc.HasValue
            && !record.RevokedAtUtc.HasValue;
    }

    private Task<IdentityOidcApplicationSessionValidationRecord?> FindOidcApplicationSessionAsync(
        Guid applicationSessionId,
        CancellationToken cancellationToken)
    {
        var parameters = IdentitySqlParameters.Create(("ApplicationSessionId", applicationSessionId));
        return queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
            IdentityOidcSessionSql.FindApplicationSessionValidationById,
            parameters,
            cancellationToken);
    }

    private bool IsOwnedActiveHostOidcSession(
        IdentityOidcApplicationSessionValidationRecord? record,
        Guid userId,
        ClaimsPrincipal principal)
    {
        if (record is null
            || record.UserId != userId
            || !string.Equals(record.ActorScope, HostScope, StringComparison.Ordinal)
            || !record.IsActive
            || record.ApplicationExpiresAtUtc <= clock.UtcNow
            || record.ApplicationRevokedAtUtc.HasValue
            || record.CenterRevokedAtUtc.HasValue
            || record.CenterExpiresAtUtc <= clock.UtcNow
            || record.LockoutEndUtc > clock.UtcNow
            || !string.Equals(
                record.CenterSecurityStamp,
                record.UserSecurityStamp,
                StringComparison.Ordinal)
            || PasswordChangeRequirementEvaluator.IsRequired(
                record.MustChangePassword,
                record.PasswordChangedAtUtc,
                clock.UtcNow,
                _identityOptions.PasswordExpirationDays))
        {
            return false;
        }

        var securityStamp = principal.FindFirstValue(IdentityClaimTypes.SecurityStamp);
        return string.IsNullOrEmpty(securityStamp)
            || string.Equals(securityStamp, record.UserSecurityStamp, StringComparison.Ordinal);
    }

    private static bool TryReadOidcIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid applicationSessionId,
        out Guid centerSessionId,
        out string clientId,
        out Guid? activeTenantId)
    {
        applicationSessionId = Guid.Empty;
        centerSessionId = Guid.Empty;
        clientId = string.Empty;
        activeTenantId = null;
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId)
            || !Guid.TryParse(
                principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId),
                out applicationSessionId)
            || !Guid.TryParse(
                principal.FindFirstValue(FullNetIdentityClaimTypes.CenterSessionId),
                out centerSessionId))
        {
            return false;
        }

        clientId = principal.FindFirstValue(FullNetIdentityClaimTypes.OidcClientId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        if (!string.Equals(
                principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse),
                IdentityOidcPrincipalFactory.TokenUseAccess,
                StringComparison.Ordinal))
        {
            return false;
        }

        var tenantClaim = principal.FindFirstValue(IdentityClaimTypes.TenantId);
        if (string.IsNullOrEmpty(tenantClaim))
        {
            return true;
        }

        if (!Guid.TryParse(tenantClaim, out var parsedTenantId))
        {
            return false;
        }

        activeTenantId = parsedTenantId;
        return true;
    }

    private static IReadOnlyCollection<string> ReadOAuthScopes(ClaimsPrincipal principal)
    {
        var scopeClaim = principal.FindFirstValue("scope");
        if (string.IsNullOrWhiteSpace(scopeClaim))
        {
            return [];
        }

        return scopeClaim
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>读取并验证上下文切换所需的用户、会话和当前租户 Claim。</summary>
    /// <param name="principal">待解析的已认证身份。</param>
    /// <param name="userId">解析得到的用户标识。</param>
    /// <param name="sessionId">解析得到的刷新会话标识。</param>
    /// <param name="activeTenantId">令牌代表的当前租户；Host 上下文为空。</param>
    /// <returns>全部必需 Claim 合法且租户 Claim 为空或为有效 Guid 时返回真。</returns>
    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid sessionId,
        out Guid? activeTenantId)
    {
        sessionId = Guid.Empty;
        activeTenantId = null;
        if (!Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            || !Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.SessionId),
                out sessionId))
        {
            return false;
        }

        var tenantClaim = principal.FindFirstValue(IdentityClaimTypes.TenantId);
        if (string.IsNullOrEmpty(tenantClaim))
        {
            return true;
        }

        if (!Guid.TryParse(tenantClaim, out var parsedTenantId))
        {
            return false;
        }

        activeTenantId = parsedTenantId;
        return true;
    }

    private static IdentityUser ToUser(RefreshSessionRecord record) => new(
        record.UserId,
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
        record.UserCreatedAtUtc,
        record.UserUpdatedAtUtc,
        record.UserVersion,
        record.PreferredLocale,
        record.ProfileVersion,
        MustChangePassword: record.MustChangePassword,
        PasswordChangedAtUtc: record.PasswordChangedAtUtc);

    private static Result<TenantContextTokenResponse> SessionNotActive() =>
        Failure(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized);

    private static Result<TenantContextTokenResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<TenantContextTokenResponse>.Failure(new Error(
            Code: code,
            Message: message,
            Type: type));
}

/// <summary>刷新会话上下文的乐观并发更新参数。</summary>
/// <param name="SessionId">刷新会话标识。</param>
/// <param name="UserId">会话所属用户标识。</param>
/// <param name="ActiveTenantId">要写入的新活动租户标识。</param>
/// <param name="ExpectedActiveTenantId">发起请求的令牌所代表的原活动租户标识。</param>
/// <param name="Version">读取会话时观察到的并发版本。</param>
internal sealed record RefreshSessionContextUpdate(
    Guid SessionId,
    Guid UserId,
    Guid? ActiveTenantId,
    Guid? ExpectedActiveTenantId,
    int Version);

/// <summary>OIDC 应用会话上下文的乐观并发更新参数。</summary>
/// <param name="ApplicationSessionId">应用会话标识。</param>
/// <param name="UserId">会话所属用户标识。</param>
/// <param name="ActiveTenantId">要写入的新活动租户标识。</param>
/// <param name="EffectiveScope">要写入的新有效作用域。</param>
/// <param name="ExpectedActiveTenantId">发起请求的令牌所代表的原活动租户标识。</param>
/// <param name="Version">读取会话时观察到的并发版本。</param>
/// <param name="NowUtc">用于过期判定的当前时间。</param>
/// <param name="UpdatedAtUtc">写入的更新时间戳。</param>
internal sealed record OidcApplicationSessionContextUpdate(
    Guid ApplicationSessionId,
    Guid UserId,
    Guid? ActiveTenantId,
    string EffectiveScope,
    Guid? ExpectedActiveTenantId,
    long Version,
    DateTimeOffset NowUtc,
    DateTimeOffset UpdatedAtUtc);
