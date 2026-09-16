using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>在 OIDC 应用会话上下文切换后签发新的 Access Token，保持协议 Issuer 与客户端授权边界。</summary>
internal sealed class IdentityOidcContextAccessTokenIssuer(
    IdentityOidcPrincipalFactory principalFactory,
    IdentityOidcSigningKeyRing keyRing,
    IOptions<IdentityOidcOptions> oidcOptions,
    IOptions<IdentityOptions> identityOptions,
    IClock clock,
    IIdGenerator idGenerator)
{
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;
    private readonly IdentityOptions _identityOptions = identityOptions.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedAccessToken Issue(IdentityOidcContextAccessTokenIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_identityOptions.AccessTokenMinutes);
        var principalRequest = new IdentityOidcPrincipalRequest(
            IdentityOidcPrincipalPurpose.ResourceApi,
            request.UserId,
            request.DisplayName,
            request.Username,
            request.CenterSessionId,
            request.ApplicationSessionId,
            request.ClientId,
            request.ActorScope,
            request.EffectiveScope,
            request.ActiveTenantId,
            request.OAuthScopes,
            request.Permissions,
            request.IsSuperAdministrator,
            request.SecurityStamp,
            request.IsExternalClient);
        var claims = principalFactory.CreateClaims(principalRequest);
        claims[JwtRegisteredClaimNames.Iss] = _oidcOptions.Issuer;
        claims[JwtRegisteredClaimNames.Aud] = request.Audience;
        claims[JwtRegisteredClaimNames.Jti] = idGenerator.NewId().ToString("D");
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _oidcOptions.Issuer,
            Audience = request.Audience,
            Claims = claims,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = keyRing.SigningCredentials,
        };
        return new IssuedAccessToken(
            _handler.CreateToken(descriptor),
            expiresAt);
    }
}

internal sealed record IdentityOidcContextAccessTokenIssueRequest(
    Guid UserId,
    string DisplayName,
    string Username,
    Guid CenterSessionId,
    Guid ApplicationSessionId,
    string ClientId,
    string ActorScope,
    string EffectiveScope,
    Guid? ActiveTenantId,
    IReadOnlyCollection<string> OAuthScopes,
    IReadOnlyCollection<string> Permissions,
    bool IsSuperAdministrator,
    string SecurityStamp,
    bool IsExternalClient,
    string Audience);