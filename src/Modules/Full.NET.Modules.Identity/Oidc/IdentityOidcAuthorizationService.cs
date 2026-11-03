using System.Security.Claims;
using System.Collections.Immutable;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

/// <summary>Identity 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcAuthorizationService(
    IdentityOidcCenterLoginService centerLoginService,
    OidcAuthenticationEventWriter authenticationEvents,
    ICommandTransaction transaction,
    IdentityOidcSessionService sessionService,
    IdentityOidcGrantRevocationService grantRevocationService,
    IdentitySessionRealtimeDelivery sessionRealtimeDelivery,
    IPermissionSnapshotReader permissionSnapshotReader,
    IOpenIddictApplicationManager applicationManager,
    IdentityOidcClientConfigResolver clientConfigResolver,
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<IdentityOptions> identityOptions,
    IOptions<IdentityOidcOptions> oidcOptions,
    ICurrentTenantContextWriter tenantContextWriter,
    IActiveTenantContextResolver? activeTenants = null,
    IOptions<DatabaseOptions>? databaseOptions = null)
{
    private const string HostScope = "host";
    private static readonly SqlStatement LockMembershipSqlServer = new("identity.oidc.lock_tenant_membership.sql_server", """
        SELECT 1 FROM fn_identity_tenant_member AS membership WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN fn_identity_user AS actor WITH (UPDLOCK, HOLDLOCK) ON actor.Id = membership.UserId
        WHERE membership.UserId = @UserId AND membership.TenantId = @TenantId AND membership.Status = 'Active'
          AND actor.IsActive = 1 AND actor.ScopeKey = 'host' AND actor.TenantId IS NULL
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement LockMembershipMySql = new("identity.oidc.lock_tenant_membership.mysql", """
        SELECT 1 FROM fn_identity_tenant_member AS membership
        INNER JOIN fn_identity_user AS actor ON actor.Id = membership.UserId
        WHERE membership.UserId = @UserId AND membership.TenantId = @TenantId AND membership.Status = 'Active'
          AND actor.IsActive = 1 AND actor.ScopeKey = 'host' AND actor.TenantId IS NULL FOR UPDATE
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement LockHostAuthorizationSqlServer = new(
        "identity.oidc.lock_host_authorization.sql_server",
        """
        SELECT permission.PermissionCode, roleObject.IsSuperAdministrator,
               identityUser.ScopeKey AS AccountScopeKey, identityUser.TenantId AS AccountTenantId, identityUser.AccountType
        FROM fn_identity_user_role AS userRole WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN fn_identity_role AS roleObject WITH (UPDLOCK, HOLDLOCK) ON roleObject.Id = userRole.RoleId
        INNER JOIN fn_identity_user AS identityUser WITH (UPDLOCK, HOLDLOCK) ON identityUser.Id = userRole.UserId
        LEFT JOIN fn_identity_role_permission AS permission WITH (UPDLOCK, HOLDLOCK) ON permission.RoleId = roleObject.Id
        WHERE userRole.UserId = @UserId AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL AND identityUser.IsActive = 1 AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL AND roleObject.IsActive = 1
        ORDER BY roleObject.Id, permission.PermissionCode
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement LockHostAuthorizationMySql = new(
        "identity.oidc.lock_host_authorization.mysql",
        """
        SELECT permission.PermissionCode, roleObject.IsSuperAdministrator,
               identityUser.ScopeKey AS AccountScopeKey, identityUser.TenantId AS AccountTenantId, identityUser.AccountType
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = userRole.RoleId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = userRole.UserId
        LEFT JOIN fn_identity_role_permission AS permission ON permission.RoleId = roleObject.Id
        WHERE userRole.UserId = @UserId AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL AND identityUser.IsActive = 1 AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL AND roleObject.IsActive = 1
        ORDER BY roleObject.Id, permission.PermissionCode FOR UPDATE
        """, SqlDataScope.HostOnly);
    private static readonly SqlStatement LockCenterSessionSqlServer = new(
        "identity.oidc.lock_center.sql_server",
        "SELECT Id, UserId, SecurityStamp, CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc, Version, UpdatedAtUtc FROM fn_identity_oidc_center_session WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id",
        SqlDataScope.HostOnly);
    private static readonly SqlStatement LockCenterSessionMySql = new(
        "identity.oidc.lock_center.mysql",
        "SELECT Id, UserId, SecurityStamp, CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc, Version, UpdatedAtUtc FROM fn_identity_oidc_center_session WHERE Id = @Id FOR UPDATE",
        SqlDataScope.HostOnly);
    private readonly IdentityOptions _identityOptions = identityOptions.Value;
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;

    public async Task<AuthenticateResult> SignInCenterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default,
        string? secondFactor = null)
    {
        // 凭据状态、中心会话和对应事件共用 Identity 本地事务。
        var (login, centerSession) = await transaction.ExecuteAsync(async token =>
        {
            var attempt = await centerLoginService.AuthenticateWithOutcomeAsync(
                    username, password, token, secondFactor)
                .ConfigureAwait(false);
            var authenticated = attempt.Login;
            if (authenticated is null)
            {
                await authenticationEvents.WriteAsync(
                    attempt.UserId, username.Trim().ToUpperInvariant(),
                    OidcAuthenticationEventWriter.CenterLogin,
                    attempt.ResultCode, false, token, authenticationMethod: attempt.ResultCode == "identity.mfa_invalid" ? "mfa" : "password")
                    .ConfigureAwait(false);
                return (Login: (IdentityOidcCenterLoginResult?)null,
                    Session: (IdentityOidcCenterSession?)null);
            }

            if (authenticated.PasswordChangeRequired)
            {
                await authenticationEvents.WriteAsync(authenticated.UserId, username.Trim().ToUpperInvariant(),
                    OidcAuthenticationEventWriter.CenterLogin, "identity.password_change_required", false, token).ConfigureAwait(false);
                return (Login: (IdentityOidcCenterLoginResult?)authenticated, Session: (IdentityOidcCenterSession?)null);
            }
            var session = await sessionService.CreateCenterSessionAsync(
                    authenticated.UserId,
                    authenticated.SecurityStamp,
                    clock.UtcNow.AddDays(_identityOptions.RefreshTokenDays),
                    token)
                .ConfigureAwait(false);
            await authenticationEvents.WriteAsync(
                authenticated.UserId, username.Trim().ToUpperInvariant(),
                OidcAuthenticationEventWriter.CenterLogin,
                "identity.oidc_center_login_succeeded", true, token,
                session.Id, authenticationMethod: authenticated.AuthenticationMethods?.Length > 1 ? "mfa" : "password")
                .ConfigureAwait(false);
            return (Login: (IdentityOidcCenterLoginResult?)authenticated,
                Session: (IdentityOidcCenterSession?)session);
        }, cancellationToken).ConfigureAwait(false);
        if (login?.PasswordChangeRequired == true) return AuthenticateResult.Fail("password_change_required");
        if (login is null || centerSession is null)
        {
            return AuthenticateResult.Fail("Invalid credentials.");
        }
        var identity = new ClaimsIdentity(
            IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(
            IdentityOidcCenterAuthenticationDefaults.CenterSessionIdClaim,
            centerSession.Id.ToString("D")));
        identity.AddClaim(new Claim(
            IdentityOidcCenterAuthenticationDefaults.UserIdClaim,
            login.UserId.ToString("D")));
        identity.AddClaim(new Claim(
            IdentityOidcCenterAuthenticationDefaults.SecurityStampClaim,
            login.SecurityStamp));
        identity.AddClaim(new Claim(
            IdentityOidcCenterAuthenticationDefaults.UsernameClaim,
            login.Username));
        identity.AddClaim(new Claim(
            IdentityOidcCenterAuthenticationDefaults.DisplayNameClaim,
            login.DisplayName));
        identity.AddClaim(new Claim(ClaimTypes.Name, login.DisplayName));
        // 使用中心会话的创建时间，滑动 Cookie 续期不得重置原始认证时间。
        identity.SetClaim(Claims.AuthenticationTime, centerSession.CreatedAtUtc.ToUnixTimeSeconds());
        identity.SetClaims("amr", (login.AuthenticationMethods ?? ["pwd"]).ToImmutableArray());
        identity.SetClaim("acr", (login.AuthenticationMethods?.Contains("otp") == true
            || login.AuthenticationMethods?.Contains("urn:fullnet:amr:recovery-code") == true) ? "urn:fullnet:acr:mfa" : "urn:fullnet:acr:password");
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(
            new AuthenticationTicket(
                principal,
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme));
    }

    public async Task<bool> IsCenterPrincipalActiveAsync(ClaimsPrincipal principal, CancellationToken token)
    {
        if (!TryReadCenterClaims(principal, out var sessionId, out var userId, out var stamp)) return false;
        return await RunInHostScopeAsync(async ct =>
        {
            var session = await sessionService.FindActiveCenterSessionAsync(sessionId, ct).ConfigureAwait(false);
            return session is not null && session.UserId == userId && session.SecurityStamp == stamp
                && await IsUserEligibleForAuthorizationAsync(userId, stamp, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    /// <summary>在展示租户前重核中心会话及 Host 切换权限，避免未授权枚举租户。</summary>
    public async Task<TenantContext?> ResolveTenantForConsentAsync(
        ClaimsPrincipal centerPrincipal, Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || activeTenants is null
            || !TryReadCenterClaims(centerPrincipal, out _, out var userId, out _)
            || !await IsCenterPrincipalActiveAsync(centerPrincipal, cancellationToken).ConfigureAwait(false))
            return null;
        var host = await permissionSnapshotReader.ReadAsync(userId, HostScope, null, cancellationToken).ConfigureAwait(false);
        if (!host.IsSuperAdministrator && !host.Permissions.Contains("tenancy.tenants.switch", StringComparer.Ordinal))
            return null;
        if (!await CanAccessTenantAsync(userId, tenantId, cancellationToken).ConfigureAwait(false)) return null;
        return await activeTenants.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClaimsPrincipal?> CreateAuthorizationPrincipalAsync(
        ClaimsPrincipal centerPrincipal,
        OpenIddictRequest request,
        CancellationToken cancellationToken = default,
        Guid? approvedTenantId = null)
    {
        ArgumentNullException.ThrowIfNull(centerPrincipal);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await CreateAuthorizationPrincipalCoreAsync(
                    centerPrincipal,
                    request,
                    cancellationToken,
                    approvedTenantId)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<ClaimsPrincipal?> CreateAuthorizationPrincipalCoreAsync(
        ClaimsPrincipal centerPrincipal,
        OpenIddictRequest request,
        CancellationToken cancellationToken,
        Guid? approvedTenantId)
    {
        if (!TryReadCenterClaims(centerPrincipal, out var centerSessionId, out var userId, out var securityStamp))
        {
            return null;
        }

        var centerSession = await sessionService.FindActiveCenterSessionAsync(
                centerSessionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (centerSession is null
            || centerSession.UserId != userId
            || !string.Equals(centerSession.SecurityStamp, securityStamp, StringComparison.Ordinal))
        {
            return null;
        }

        if (!await IsUserEligibleForAuthorizationAsync(userId, centerSession.SecurityStamp, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        var application = await applicationManager.FindByClientIdAsync(
                request.ClientId ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            return null;
        }

        var applicationId = Guid.Parse(
            await applicationManager.GetIdAsync(application, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("OIDC application id missing."));
        var resolvedClient = await clientConfigResolver.ResolveAsync(
                request.ClientId ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        if (resolvedClient?.IsDisabled == true)
        {
            return null;
        }

        var audience = string.IsNullOrWhiteSpace(resolvedClient?.ResourceAudience)
            ? _identityOptions.Audience
            : resolvedClient!.ResourceAudience!;
        var hostAuthorization = await permissionSnapshotReader.ReadAsync(
                userId,
                HostScope,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (approvedTenantId is { } tenantId)
        {
            // 目标只能来自已验证的交互同意；发行时仍须重核 Host 权限和租户活动状态。
            if (tenantId == Guid.Empty || activeTenants is null
                || (!hostAuthorization.IsSuperAdministrator && !hostAuthorization.Permissions.Contains("tenancy.tenants.switch", StringComparer.Ordinal))
                || !await CanAccessTenantAsync(userId, tenantId, cancellationToken).ConfigureAwait(false)
                || await activeTenants.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
                return null;
        }
        var effectiveScope = approvedTenantId is { } selectedTenant ? $"tenant:{selectedTenant:N}" : HostScope;
        var authorization = approvedTenantId is null ? hostAuthorization : await permissionSnapshotReader.ReadAsync(
                userId, HostScope, approvedTenantId, cancellationToken).ConfigureAwait(false);
        var applicationSession = await transaction.ExecuteAsync<IdentityOidcApplicationSession?>(async token =>
        {
            if (approvedTenantId is not null)
            {
                // 角色和权限行保持锁定至会话提交，避免撤权在快照读取后先于发行提交。
                var lockedAuthorization = await queryExecutor.QueryAsync<IdentityAuthorizationRow>(
                    databaseOptions?.Value.Provider == DatabaseProvider.MySql
                        ? LockHostAuthorizationMySql : LockHostAuthorizationSqlServer,
                    IdentitySqlParameters.Create(("UserId", userId)), token).ConfigureAwait(false);
                if (!lockedAuthorization.Any(row => row.IsSuperAdministrator
                    || string.Equals(row.PermissionCode, "tenancy.tenants.switch", StringComparison.Ordinal)))
                {
                    // 默认自助能力必须持有活动成员行；锁定至发行提交，避免接受同意期间成员被移除。
                    var member = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                        databaseOptions?.Value.Provider == DatabaseProvider.MySql ? LockMembershipMySql : LockMembershipSqlServer,
                        IdentitySqlParameters.Create(("UserId", userId), ("TenantId", approvedTenantId.Value)), token).ConfigureAwait(false);
                    if (member != 1) return null;
                }

                // 与中心会话撤销串行化，并在持锁后再次检查用户安全状态。
                var lockedCenter = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCenterSessionRow>(
                    databaseOptions?.Value.Provider == DatabaseProvider.MySql
                        ? LockCenterSessionMySql : LockCenterSessionSqlServer,
                    IdentitySqlParameters.Create(("Id", centerSessionId)), token).ConfigureAwait(false);
                if (lockedCenter is null || lockedCenter.UserId != userId || lockedCenter.RevokedAtUtc.HasValue
                    || lockedCenter.ExpiresAtUtc <= clock.UtcNow
                    || !string.Equals(lockedCenter.SecurityStamp, securityStamp, StringComparison.Ordinal)
                    || !await IsUserEligibleForAuthorizationAsync(userId, securityStamp, token).ConfigureAwait(false))
                    return null;
            }
            var session = await sessionService.CreateApplicationSessionAsync(
                    centerSessionId,
                    applicationId,
                    request.ClientId!,
                    userId,
                    HostScope,
                    effectiveScope,
                    approvedTenantId,
                    clock.UtcNow.AddMinutes(_identityOptions.AccessTokenMinutes),
                    token)
                .ConfigureAwait(false);
            await authenticationEvents.WriteAsync(userId, null, OidcAuthenticationEventWriter.CenterSessionReused,
                "identity.oidc_center_session_reused", true, token, centerSessionId, request.ClientId, session.Id,
                authenticationMethod: "sso").ConfigureAwait(false);
            await authenticationEvents.WriteAsync(
                userId, null,
                OidcAuthenticationEventWriter.ApplicationSessionCreated,
                "identity.oidc_application_session_created", true, token,
                centerSessionId, request.ClientId, session.Id)
                .ConfigureAwait(false);
            return session;
        }, cancellationToken).ConfigureAwait(false);
        if (applicationSession is null) return null;
        var scopes = (request.Scope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        var displayName = centerPrincipal.FindFirst(
                IdentityOidcCenterAuthenticationDefaults.DisplayNameClaim)?.Value
            ?? centerPrincipal.FindFirst(ClaimTypes.Name)?.Value
            ?? userId.ToString("D");
        var username = centerPrincipal.FindFirst(
                IdentityOidcCenterAuthenticationDefaults.UsernameClaim)?.Value
            ?? userId.ToString("D");
        identity.SetClaim(Claims.Subject, userId.ToString("D"));
        identity.SetClaim(Claims.Name, displayName);
        identity.SetClaim(Claims.PreferredUsername, username);
        identity.SetClaim(Claims.AuthenticationTime, centerSession.CreatedAtUtc.ToUnixTimeSeconds());
        identity.SetClaims("amr", centerPrincipal.GetClaims("amr"));
        identity.SetClaim("acr", centerPrincipal.GetClaim("acr"));
        identity.SetClaim(FullNetIdentityClaimTypes.CenterSessionId, centerSessionId.ToString("D"));
        identity.SetClaim(
            FullNetIdentityClaimTypes.ApplicationSessionId,
            applicationSession.Id.ToString("D"));
        identity.SetClaim(FullNetIdentityClaimTypes.OidcClientId, request.ClientId!);
        identity.SetClaim(IdentityClaimTypes.ActorScope, HostScope);
        identity.SetClaim(IdentityClaimTypes.Scope, effectiveScope);
        if (approvedTenantId is { } selectedId)
            identity.SetClaim(IdentityClaimTypes.TenantId, selectedId.ToString("D"));
        identity.SetClaim(JwtRegisteredClaimNames.Aud, audience);
        identity.SetClaim("fullnet_is_first_party", resolvedClient?.IsFirstParty ?? true);
        identity.SetClaim("fullnet_security_stamp", securityStamp);
        identity.SetClaim("fullnet_is_super_admin", authorization.IsSuperAdministrator);
        identity.SetClaim(
            "fullnet_permissions",
            string.Join(
                ',',
                authorization.Permissions
                    .Where(permission => !string.IsNullOrWhiteSpace(permission))
                    .OrderBy(permission => permission, StringComparer.Ordinal)));
        identity.SetClaim("fullnet_oauth_scopes", string.Join(' ', scopes));
        identity.SetScopes(scopes);
        // 对外 Claim 投影统一交给签发处理器；授权码保留内部状态，但不直接向客户端披露。
        return new ClaimsPrincipal(identity);
    }

    public async Task<bool> SignOutApplicationAsync(
        HttpContext httpContext,
        string clientId,
        Guid authenticatedAccessUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var userId = authenticatedAccessUserId;
        if (userId == Guid.Empty || !Guid.TryParse(httpContext.User.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId), out var sessionId)
            || sessionId == Guid.Empty || httpContext.User.FindFirstValue(FullNetIdentityClaimTypes.OidcClientId) != clientId)
        {
            return false;
        }

        return await RunInHostScopeAsync(async token =>
        {
            var change = await transaction.ExecuteAsync<(bool Allowed, IReadOnlyList<Guid> Ids)>(async transactionToken =>
            {
                var application = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionRow>(
                    IdentityOidcSessionSql.FindApplicationSessionById, IdentitySqlParameters.Create(("Id", sessionId)), transactionToken).ConfigureAwait(false);
                if (application is null || application.UserId != userId || application.ClientId != clientId) return (false, []);
                var revoked = await sessionService.RevokeApplicationSessionAsync(sessionId, transactionToken).ConfigureAwait(false);
                IReadOnlyList<Guid> ids = revoked ? [sessionId] : [];
                if (ids.Count > 0)
                {
                    // 当前应用退出不撤回永久同意或同客户端的其他设备；旧凭据由该应用会话的权威状态拒绝。
                    await authenticationEvents.WriteAsync(
                        userId, null,
                        OidcAuthenticationEventWriter.ApplicationLogout,
                        "identity.oidc_application_logout_succeeded", true, transactionToken,
                        centerSessionId: application.CenterSessionId, clientId: clientId, applicationSessionId: sessionId)
                        .ConfigureAwait(false);
                }

                return (true, ids);
            }, token).ConfigureAwait(false);
            if (!change.Allowed) return false;
            if (change.Ids.Count > 0)
            {
                await sessionRealtimeDelivery.PublishSessionsRevokedAsync(
                        userId,
                        change.Ids,
                        token)
                    .ConfigureAwait(false);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>标准浏览器退出仅撤销当前中心会话；其他设备的授权仍由各自会话决定。</summary>
    public async Task SignOutCurrentCenterAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var authentication = await httpContext.AuthenticateAsync(
            IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (authentication.Principal?.Identity?.IsAuthenticated == true
            && TryReadCenterLogoutClaims(authentication.Principal, out var centerSessionId, out var userId))
        {
            var sessionIds = await RunInHostScopeAsync(token => transaction.ExecuteAsync(async transactionToken =>
            {
                var center = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCenterSessionRow>(
                    IdentityOidcSessionSql.FindCenterSessionById,
                    IdentitySqlParameters.Create(("Id", centerSessionId)), transactionToken).ConfigureAwait(false);
                if (center is null || center.UserId != userId || center.RevokedAtUtc.HasValue)
                    return Array.Empty<Guid>();
                var ids = await queryExecutor.QueryAsync<Guid>(
                    IdentityOidcSessionSql.ListApplicationSessionIdsByCenterSession,
                    IdentitySqlParameters.Create(("CenterSessionId", centerSessionId), ("UserId", userId)),
                    transactionToken).ConfigureAwait(false);
                // 授权码/刷新兑换及资源 API 均检查权威会话，不能按用户撤销所有设备的 grant。
                await sessionService.RevokeCenterSessionAsync(centerSessionId, transactionToken).ConfigureAwait(false);
                await authenticationEvents.WriteAsync(userId, null, OidcAuthenticationEventWriter.CenterLogout,
                    "identity.oidc_center_logout_succeeded", true, transactionToken, centerSessionId)
                    .ConfigureAwait(false);
                return ids.ToArray();
            }, token), cancellationToken).ConfigureAwait(false);
            if (sessionIds.Length > 0)
                await sessionRealtimeDelivery.PublishSessionsRevokedAsync(userId, sessionIds, cancellationToken)
                    .ConfigureAwait(false);
        }

        await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
    }

    public async Task SignOutCenterAsync(
        HttpContext httpContext,
        Guid? authenticatedAccessUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var centerUserId = await TryReadAuthenticatedCenterUserAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (authenticatedAccessUserId == Guid.Empty)
        {
            return;
        }

        // 令牌所属用户决定撤销目标；共享 Cookie 只决定是否清除浏览器中心票据。
        if ((authenticatedAccessUserId ?? centerUserId) is Guid userId)
        {
            await RunInHostScopeAsync(async token =>
            {
                var applicationSessionIds = await sessionService.ListActiveHostApplicationSessionIdsByUserAsync(
                        userId,
                        token)
                    .ConfigureAwait(false);
                await transaction.ExecuteAsync(async transactionToken =>
                {
                    var count = await sessionService.RevokeAllCenterSessionsByUserAsync(
                            userId, transactionToken)
                        .ConfigureAwait(false);
                    var grants = await grantRevocationService.RevokeByUserIdAsync(
                            userId, transactionToken)
                        .ConfigureAwait(false);
                    if (count > 0 || applicationSessionIds.Count > 0
                        || grants.TokensRevoked > 0 || grants.AuthorizationsRevoked > 0)
                    {
                        await authenticationEvents.WriteAsync(
                            userId, null,
                            OidcAuthenticationEventWriter.CenterLogout,
                            "identity.oidc_center_logout_succeeded", true, transactionToken)
                            .ConfigureAwait(false);
                    }

                    return count;
                }, token).ConfigureAwait(false);
                if (applicationSessionIds.Count > 0)
                {
                    await sessionRealtimeDelivery.PublishSessionsRevokedAsync(
                            userId,
                            applicationSessionIds,
                            token)
                        .ConfigureAwait(false);
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        if (authenticatedAccessUserId is null || centerUserId == authenticatedAccessUserId)
        {
            await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
        }
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

    private async Task<bool> IsUserEligibleForAuthorizationAsync(
        Guid userId,
        string centerSessionSecurityStamp,
        CancellationToken cancellationToken)
    {
        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return user is not null
            && user.IsActive
            && !(user.LockoutEndUtc > clock.UtcNow)
            && string.Equals(user.SecurityStamp, centerSessionSecurityStamp, StringComparison.Ordinal)
            && !PasswordChangeRequirementEvaluator.IsRequired(
                user.MustChangePassword,
                user.PasswordChangedAtUtc,
                clock.UtcNow,
                _identityOptions.PasswordExpirationDays);
    }

    private static bool TryReadCenterClaims(
        ClaimsPrincipal principal,
        out Guid centerSessionId,
        out Guid userId,
        out string securityStamp)
    {
        centerSessionId = default;
        userId = default;
        securityStamp = string.Empty;
        var centerSessionClaim = principal.FindFirst(
            IdentityOidcCenterAuthenticationDefaults.CenterSessionIdClaim)?.Value;
        var userIdClaim = principal.FindFirst(
            IdentityOidcCenterAuthenticationDefaults.UserIdClaim)?.Value;
        securityStamp = principal.FindFirst(
            IdentityOidcCenterAuthenticationDefaults.SecurityStampClaim)?.Value ?? string.Empty;
        return Guid.TryParse(centerSessionClaim, out centerSessionId)
            && Guid.TryParse(userIdClaim, out userId)
            && !string.IsNullOrWhiteSpace(securityStamp);
    }

    private static bool TryReadCenterLogoutClaims(
        ClaimsPrincipal principal,
        out Guid centerSessionId,
        out Guid userId)
    {
        centerSessionId = default;
        userId = default;
        var centerSessionClaim = principal.FindFirst(
            IdentityOidcCenterAuthenticationDefaults.CenterSessionIdClaim)?.Value;
        var userIdClaim = principal.FindFirst(
            IdentityOidcCenterAuthenticationDefaults.UserIdClaim)?.Value;
        return Guid.TryParse(centerSessionClaim, out centerSessionId)
            && Guid.TryParse(userIdClaim, out userId);
    }

    private async Task<bool> CanAccessTenantAsync(Guid userId, Guid tenantId, CancellationToken token)
    {
        var access = await new IdentityTenantContextAccessReader(queryExecutor).ReadAsync(userId, token).ConfigureAwait(false);
        return access.AllTenants || access.TenantIds.Contains(tenantId);
    }

    private static async Task<Guid?> TryReadAuthenticatedCenterUserAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var centerAuth = await httpContext.AuthenticateAsync(
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        var principal = centerAuth.Principal;
        if (principal?.Identity?.IsAuthenticated != true
            || !TryReadCenterLogoutClaims(principal, out _, out var userId))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return userId;
    }
}
