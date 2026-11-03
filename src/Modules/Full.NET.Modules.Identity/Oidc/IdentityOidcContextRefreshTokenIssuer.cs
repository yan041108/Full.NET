using System.Collections.Immutable;
using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>在 OIDC 上下文切换后轮换 refresh token，避免客户端继续持有旧作用域的离线凭据。</summary>
internal sealed class IdentityOidcContextRefreshTokenIssuer(
    IOpenIddictServerDispatcher dispatcher,
    IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    ILogger<IdentityOidcContextRefreshTokenIssuer> logger,
    IClock clock)
{
    public async Task<string?> TryIssueAsync(
        ClaimsPrincipal sourcePrincipal,
        IdentityOidcContextAccessTokenIssueRequest issueRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePrincipal);
        ArgumentNullException.ThrowIfNull(issueRequest);
        if (!issueRequest.OAuthScopes.Contains(Scopes.OfflineAccess, StringComparer.Ordinal))
        {
            return null;
        }

        var principal = BuildRefreshPrincipal(issueRequest);
        var options = serverOptions.CurrentValue;
        // 手动 GenerateToken 不经过完整 SignIn 管线；必须补组件读取的私有 issuer，不能只设置公共 iss。
        // 每次读取当前选项，避免密钥轮换后仍使用 IOptions 的首次缓存凭据。
        principal.SetClaim(Claims.Private.Issuer, options.Issuer?.AbsoluteUri
            ?? throw new InvalidOperationException("The OIDC refresh issuer must be explicitly configured."));
        principal.SetAuthorizationId(sourcePrincipal.GetAuthorizationId());
        var createdAt = clock.UtcNow;
        principal.SetCreationDate(createdAt);
        principal.SetExpirationDate(createdAt + (options.RefreshTokenLifetime ?? TimeSpan.FromDays(7)));
        var transaction = new OpenIddictServerTransaction
        {
            Options = options,
            Logger = logger,
            Request = new OpenIddictRequest
            {
                ClientId = issueRequest.ClientId,
                GrantType = GrantTypes.RefreshToken,
            },
        };
        var notification = new OpenIddictServerEvents.GenerateTokenContext(transaction)
        {
            ClientId = issueRequest.ClientId,
            CreateTokenEntry = !options.DisableTokenStorage,
            IsReferenceToken = options.UseReferenceRefreshTokens,
            PersistTokenPayload = options.UseReferenceRefreshTokens,
            Principal = principal,
            TokenFormat = TokenFormats.Private.JsonWebToken,
            TokenType = TokenTypeIdentifiers.RefreshToken,
        };
        await dispatcher.DispatchAsync(notification).ConfigureAwait(false);
        if (notification.IsRejected)
        {
            return null;
        }

        return notification.Token;
    }

    private static ClaimsPrincipal BuildRefreshPrincipal(IdentityOidcContextAccessTokenIssueRequest issueRequest)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, issueRequest.UserId.ToString("D"));
        identity.SetClaim(Claims.Name, issueRequest.DisplayName);
        identity.SetClaim(Claims.PreferredUsername, issueRequest.Username);
        identity.SetClaim(FullNetIdentityClaimTypes.CenterSessionId, issueRequest.CenterSessionId.ToString("D"));
        identity.SetClaim(
            FullNetIdentityClaimTypes.ApplicationSessionId,
            issueRequest.ApplicationSessionId.ToString("D"));
        identity.SetClaim(FullNetIdentityClaimTypes.OidcClientId, issueRequest.ClientId);
        identity.SetClaim(IdentityClaimTypes.ActorScope, issueRequest.ActorScope);
        identity.SetClaim(IdentityClaimTypes.Scope, issueRequest.EffectiveScope);
        if (issueRequest.ActiveTenantId is Guid tenantId)
        {
            identity.SetClaim(FullNetIdentityClaimTypes.TenantId, tenantId.ToString("D"));
        }

        identity.SetClaim(JwtRegisteredClaimNames.Aud, issueRequest.Audience);
        if (issueRequest.AuthenticationTime is long authenticatedAt) identity.SetClaim("auth_time", authenticatedAt);
        identity.SetClaim("acr", issueRequest.AuthenticationContext);
        identity.SetClaims("amr", (issueRequest.AuthenticationMethods ?? []).ToImmutableArray());
        identity.SetClaim("fullnet_is_first_party", !issueRequest.IsExternalClient);
        identity.SetClaim("fullnet_security_stamp", issueRequest.SecurityStamp);
        identity.SetClaim("fullnet_is_super_admin", issueRequest.IsSuperAdministrator);
        identity.SetClaim(
            "fullnet_permissions",
            string.Join(
                ',',
                issueRequest.Permissions
                    .Where(permission => !string.IsNullOrWhiteSpace(permission))
                    .OrderBy(permission => permission, StringComparer.Ordinal)));
        identity.SetClaim(
            "fullnet_oauth_scopes",
            string.Join(' ', issueRequest.OAuthScopes));
        identity.SetScopes(issueRequest.OAuthScopes.ToImmutableArray());
        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Name or Claims.PreferredUsername => claim.Subject!.HasScope(Scopes.Profile)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [],
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            JwtRegisteredClaimNames.Aud => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [],
        });
        return new ClaimsPrincipal(identity);
    }
}
