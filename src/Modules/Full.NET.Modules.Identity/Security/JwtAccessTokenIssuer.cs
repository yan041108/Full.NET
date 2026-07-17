using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Security;

internal sealed class JwtAccessTokenIssuer(
    IOptions<IdentityOptions> options,
    RsaSigningKeyRing keyRing,
    IClock clock,
    IIdGenerator idGenerator) : IAccessTokenIssuer
{
    private readonly IdentityOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedAccessToken Issue(
        IdentityUser user,
        Guid sessionId,
        Guid? activeTenantId,
        IReadOnlyCollection<string> permissions)
    {
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);
        var effectiveTenantId = activeTenantId ?? user.TenantId;
        var effectiveScope = effectiveTenantId.HasValue
            ? $"tenant:{effectiveTenantId.Value:N}"
            : user.ScopeKey;
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString("D"),
            [JwtRegisteredClaimNames.Jti] = idGenerator.NewId().ToString("D"),
            [JwtRegisteredClaimNames.Name] = user.DisplayName,
            ["preferred_username"] = user.Username,
            ["client_id"] = _options.ClientId,
            [IdentityClaimTypes.SessionId] = sessionId.ToString("D"),
            [IdentityClaimTypes.ActorScope] = user.ScopeKey,
            [IdentityClaimTypes.Scope] = effectiveScope,
            [IdentityClaimTypes.SecurityStamp] = user.SecurityStamp,
        };
        if (effectiveTenantId.HasValue)
        {
            claims[IdentityClaimTypes.TenantId] = effectiveTenantId.Value.ToString("D");
        }

        var normalizedPermissions = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();
        if (normalizedPermissions.Length > 0)
        {
            claims[IdentityClaimTypes.Permission] = normalizedPermissions;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
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
