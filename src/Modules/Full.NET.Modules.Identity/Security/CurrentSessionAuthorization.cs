using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>交互工具逐次重查身份与权限；不把已验证 JWT 的权限声明作为长期授权。</summary>
internal sealed class CurrentSessionAuthorization(
    IHttpContextAccessor http,
    AccessSessionValidator sessions,
    IPermissionSnapshotReader permissions,
    ICurrentTenant tenant,
    IActiveTenantContextResolver tenants,
    IClock clock,
    IOptions<IdentityOidcOptions> oidcOptions) : ICurrentSessionAuthorization
{
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;

    /// <inheritdoc />
    public async Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || principal.HasClaim(claim => claim.Type == FullNetIdentityClaimTypes.ApiKeyId)
            || principal.FindFirstValue(FullNetIdentityClaimTypes.PasswordChangeRequired) == "true"
            || !long.TryParse(principal.FindFirstValue("exp"), out var expires)
            || expires <= clock.UtcNow.ToUnixTimeSeconds()
            || !await sessions.IsValidAsync(principal, cancellationToken).ConfigureAwait(false)) return null;
        if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var userId)
            || !TryReadSessionId(principal, out var sessionId)) return null;
        var tenantClaim = principal.FindFirstValue(FullNetIdentityClaimTypes.TenantId);
        Guid? tenantId = Guid.TryParse(tenantClaim, out var parsed) ? parsed : null;
        if (tenantId != tenant.Id || (tenantId is null && !tenant.IsHost)) return null;
        if (tenantId is { } id && await tenants.ResolveActiveByIdAsync(id, cancellationToken).ConfigureAwait(false) is null) return null;
        var snapshot = await permissions.ReadAsync(userId,
            principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope)!, tenantId, cancellationToken).ConfigureAwait(false);
        return snapshot.Permissions.Contains(permissionCode, StringComparer.Ordinal)
            ? new(userId, tenantId, sessionId) : null;
    }

    private bool TryReadSessionId(ClaimsPrincipal principal, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        var issuer = principal.FindFirstValue(JwtRegisteredClaimNames.Iss);
        if (_oidcOptions.Enable
            && !string.IsNullOrWhiteSpace(_oidcOptions.Issuer)
            && string.Equals(issuer, _oidcOptions.Issuer, StringComparison.Ordinal))
        {
            return Guid.TryParse(
                principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId),
                out sessionId);
        }

        return Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SessionId), out sessionId);
    }
}
