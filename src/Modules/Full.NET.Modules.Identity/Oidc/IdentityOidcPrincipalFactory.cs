using Full.NET.Modules.Identity.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Oidc;

internal enum IdentityOidcPrincipalPurpose
{
    ResourceApi,
    IdToken,
    UserInfo,
}

internal sealed record IdentityOidcPrincipalRequest(
    IdentityOidcPrincipalPurpose Purpose,
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
    IReadOnlyCollection<string> FullNetPermissions,
    bool IsSuperAdministrator,
    string SecurityStamp,
    bool IsExternalClient);

internal sealed class IdentityOidcPrincipalFactory
{
    internal const string TokenUseAccess = "access";
    internal const string TokenUseId = "id";

    public void EnsureResourceApiPurpose(IdentityOidcPrincipalPurpose purpose)
    {
        if (purpose == IdentityOidcPrincipalPurpose.IdToken)
        {
            throw new InvalidOperationException(
                "IdToken purpose cannot be projected for resource API tokens.");
        }
    }

    public Dictionary<string, object> CreateClaims(IdentityOidcPrincipalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tokenUse = request.Purpose switch
        {
            IdentityOidcPrincipalPurpose.IdToken => TokenUseId,
            _ => TokenUseAccess,
        };
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [JwtRegisteredClaimNames.Sub] = request.UserId.ToString("D"),
            [JwtRegisteredClaimNames.Name] = request.DisplayName,
            ["preferred_username"] = request.Username,
            [FullNetIdentityClaimTypes.OidcClientId] = request.ClientId,
            [FullNetIdentityClaimTypes.TokenUse] = tokenUse,
            [FullNetIdentityClaimTypes.CenterSessionId] = request.CenterSessionId.ToString("D"),
            [FullNetIdentityClaimTypes.ApplicationSessionId] = request.ApplicationSessionId.ToString("D"),
            [FullNetIdentityClaimTypes.ActorScope] = request.ActorScope,
            [FullNetIdentityClaimTypes.Scope] = request.EffectiveScope,
        };
        if (request.OAuthScopes.Count > 0)
        {
            claims["scope"] = string.Join(
                ' ',
                request.OAuthScopes
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(scope => scope, StringComparer.Ordinal));
        }

        if (request.ActiveTenantId.HasValue)
        {
            claims[FullNetIdentityClaimTypes.TenantId] = request.ActiveTenantId.Value.ToString("D");
        }

        if (request.IsExternalClient)
        {
            return claims;
        }

        claims[FullNetIdentityClaimTypes.SecurityStamp] = request.SecurityStamp;
        if (request.IsSuperAdministrator)
        {
            claims[FullNetIdentityClaimTypes.SuperAdministrator] = true;
        }
        else if (request.FullNetPermissions.Count > 0)
        {
            claims[FullNetIdentityClaimTypes.Permission] = request.FullNetPermissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => permission, StringComparer.Ordinal)
                .ToArray();
        }

        return claims;
    }
}