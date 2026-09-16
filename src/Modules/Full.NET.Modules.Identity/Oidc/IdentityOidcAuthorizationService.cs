using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
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

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcAuthorizationService(
    IdentityOidcCenterLoginService centerLoginService,
    IdentityOidcSessionService sessionService,
    IdentityOidcGrantRevocationService grantRevocationService,
    IdentitySessionRealtimeDelivery sessionRealtimeDelivery,
    IPermissionSnapshotReader permissionSnapshotReader,
    IOpenIddictApplicationManager applicationManager,
    IdentityOidcClientConfigResolver clientConfigResolver,
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<IdentityOptions> identityOptions,
    IOptions<IdentityOidcOptions> oidcOptions)
{
    private const string HostScope = "host";
    private readonly IdentityOptions _identityOptions = identityOptions.Value;
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;

    public async Task<AuthenticateResult> SignInCenterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var login = await centerLoginService.AuthenticateAsync(
                username,
                password,
                cancellationToken)
            .ConfigureAwait(false);
        if (login is null)
        {
            return AuthenticateResult.Fail("Invalid credentials.");
        }

        var centerSession = await sessionService.CreateCenterSessionAsync(
                login.UserId,
                login.SecurityStamp,
                clock.UtcNow.AddDays(_identityOptions.RefreshTokenDays),
                cancellationToken)
            .ConfigureAwait(false);
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
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(
            new AuthenticationTicket(
                principal,
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme));
    }

    public async Task<ClaimsPrincipal?> CreateAuthorizationPrincipalAsync(
        ClaimsPrincipal centerPrincipal,
        OpenIddictRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(centerPrincipal);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await CreateAuthorizationPrincipalCoreAsync(
                    centerPrincipal,
                    request,
                    cancellationToken)
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
        CancellationToken cancellationToken)
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
        var authorization = await permissionSnapshotReader.ReadAsync(
                userId,
                HostScope,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var applicationSession = await sessionService.CreateApplicationSessionAsync(
                centerSessionId,
                applicationId,
                request.ClientId!,
                userId,
                HostScope,
                HostScope,
                null,
                clock.UtcNow.AddMinutes(_identityOptions.AccessTokenMinutes),
                cancellationToken)
            .ConfigureAwait(false);
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
        identity.SetClaim(FullNetIdentityClaimTypes.CenterSessionId, centerSessionId.ToString("D"));
        identity.SetClaim(
            FullNetIdentityClaimTypes.ApplicationSessionId,
            applicationSession.Id.ToString("D"));
        identity.SetClaim(FullNetIdentityClaimTypes.OidcClientId, request.ClientId!);
        identity.SetClaim(IdentityClaimTypes.ActorScope, HostScope);
        identity.SetClaim(IdentityClaimTypes.Scope, HostScope);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var userId = await TryReadAuthenticatedCenterUserAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (userId is null)
        {
            return false;
        }

        var revokedSessionIds = await sessionService.RevokeActiveApplicationSessionsByUserAndClientAsync(
                userId.Value,
                clientId,
                cancellationToken)
            .ConfigureAwait(false);
        if (revokedSessionIds.Count > 0)
        {
            await grantRevocationService.RevokeByUserAndClientAsync(
                    userId.Value,
                    clientId,
                    cancellationToken)
                .ConfigureAwait(false);
            await sessionRealtimeDelivery.PublishSessionsRevokedAsync(
                    userId.Value,
                    revokedSessionIds,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    public async Task SignOutCenterAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var centerUserId = await TryReadAuthenticatedCenterUserAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (centerUserId is Guid userId)
        {
            var applicationSessionIds = await sessionService.ListActiveHostApplicationSessionIdsByUserAsync(
                    userId,
                    cancellationToken)
                .ConfigureAwait(false);
            await sessionService.RevokeAllCenterSessionsByUserAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            await grantRevocationService.RevokeByUserIdAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            if (applicationSessionIds.Count > 0)
            {
                await sessionRealtimeDelivery.PublishSessionsRevokedAsync(
                        userId,
                        applicationSessionIds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
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
