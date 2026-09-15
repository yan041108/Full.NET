using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
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
    IPermissionSnapshotReader permissionSnapshotReader,
    IOpenIddictApplicationManager applicationManager,
    IdentityOidcClientConfigResolver clientConfigResolver,
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
        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Name or Claims.PreferredUsername or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });
        return new ClaimsPrincipal(identity);
    }

    public async Task SignOutCenterAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var principal = httpContext.User;
        if (principal.Identity?.IsAuthenticated == true
            && TryReadCenterLogoutClaims(principal, out var centerSessionId, out var userId))
        {
            await sessionService.RevokeCenterSessionAsync(centerSessionId, cancellationToken)
                .ConfigureAwait(false);
            await grantRevocationService.RevokeByUserIdAsync(userId, cancellationToken)
                .ConfigureAwait(false);
        }

        await httpContext.SignOutAsync(IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
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
}