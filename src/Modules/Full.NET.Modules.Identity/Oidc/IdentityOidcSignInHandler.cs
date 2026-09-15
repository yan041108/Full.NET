using System.Security.Claims;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcSignInHandler(
    IdentityOidcPrincipalFactory principalFactory,
    IOptions<IdentityOptions> identityOptions,
    IOptions<IdentityOidcOptions> oidcOptions) : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.ProcessSignInContext>()
            .UseSingletonHandler<IdentityOidcSignInHandler>()
            .SetOrder(OpenIddictServerHandlers.PrepareAccessTokenPrincipal.Descriptor.Order - 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return default;
        }

        if (!TryReadAuthorizationContext(identity, out var principalRequest, out var audience))
        {
            return default;
        }

        foreach (var (key, value) in principalFactory.CreateClaims(principalRequest))
        {
            identity.SetClaim(key, value?.ToString());
        }

        identity.SetClaim(JwtRegisteredClaimNames.Iss, oidcOptions.Value.Issuer);
        identity.SetClaim(JwtRegisteredClaimNames.Aud, audience);
        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Name or Claims.PreferredUsername or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            JwtRegisteredClaimNames.Aud or JwtRegisteredClaimNames.Iss => [Destinations.AccessToken, Destinations.IdentityToken],
            FullNetIdentityClaimTypes.TokenUse => claim.Value?.ToString() == IdentityOidcPrincipalFactory.TokenUseId
                ? [Destinations.IdentityToken]
                : [Destinations.AccessToken],
            _ => [Destinations.AccessToken],
        });
        context.Principal = new ClaimsPrincipal(identity);
        return default;
    }

    private bool TryReadAuthorizationContext(
        ClaimsIdentity identity,
        out IdentityOidcPrincipalRequest principalRequest,
        out string audience)
    {
        principalRequest = null!;
        audience = identityOptions.Value.Audience;
        if (!Guid.TryParse(identity.GetClaim(Claims.Subject), out var userId)
            || !Guid.TryParse(identity.GetClaim(FullNetIdentityClaimTypes.CenterSessionId), out var centerSessionId)
            || !Guid.TryParse(identity.GetClaim(FullNetIdentityClaimTypes.ApplicationSessionId), out var applicationSessionId))
        {
            return false;
        }

        var clientId = identity.GetClaim(FullNetIdentityClaimTypes.OidcClientId) ?? string.Empty;
        var clientConfig = oidcOptions.Value.Clients.FirstOrDefault(
            client => string.Equals(client.ClientId, clientId, StringComparison.Ordinal));
        audience = string.IsNullOrWhiteSpace(clientConfig?.ResourceAudience)
            ? identity.GetClaim(JwtRegisteredClaimNames.Aud) ?? identityOptions.Value.Audience
            : clientConfig.ResourceAudience;
        var oauthScopes = (identity.GetClaim("fullnet_oauth_scopes") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var permissions = (identity.GetClaim("fullnet_permissions") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isSuperAdministrator = string.Equals(
            identity.GetClaim("fullnet_is_super_admin"),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);
        var isExternalClient = !string.Equals(
            identity.GetClaim("fullnet_is_first_party"),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);
        principalRequest = new IdentityOidcPrincipalRequest(
            IdentityOidcPrincipalPurpose.ResourceApi,
            userId,
            identity.GetClaim(Claims.Name) ?? userId.ToString("D"),
            identity.GetClaim(Claims.PreferredUsername) ?? userId.ToString("D"),
            centerSessionId,
            applicationSessionId,
            clientId,
            identity.GetClaim(IdentityClaimTypes.ActorScope) ?? "host",
            identity.GetClaim(IdentityClaimTypes.Scope) ?? "host",
            null,
            oauthScopes,
            permissions,
            isSuperAdministrator,
            identity.GetClaim("fullnet_security_stamp") ?? string.Empty,
            isExternalClient);
        return true;
    }
}
