using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Security;

/// <summary>已认证凭据的再次授权使用权威状态，不复用认证开始时的旧权限声明。</summary>
/// <param name="contexts">当前请求主体。</param>
/// <param name="tenant">认证管线建立的可信范围。</param>
/// <param name="query">Identity 自有查询执行器。</param>
/// <param name="clock">权威 UTC 时钟。</param>
internal sealed class CurrentApiKeyAuthorization(IHttpContextAccessor contexts, ICurrentTenant tenant, IQueryExecutor query, IClock clock)
    : ICurrentApiKeyAuthorization
{
    /// <inheritdoc/>
    public async Task<Guid?> AuthorizeAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var principal = contexts.HttpContext?.User;
        if (permissions.Count == 0 || permissions.Any(string.IsNullOrWhiteSpace)
            || principal?.Identity is not { IsAuthenticated: true, AuthenticationType: "FullNET.ApiKey" or "FullNET.Signature" }
            || !Guid.TryParse(principal.FindFirst("sub")?.Value, out var user) || user == Guid.Empty
            || !Guid.TryParse(principal.FindFirst(IdentityClaimTypes.ApiKeyId)?.Value, out var key) || key == Guid.Empty
            || permissions.Any(permission => !principal.HasClaim(IdentityClaimTypes.Permission, permission))) return null;
        var parameters = IdentitySqlParameters.Create(("ApiKeyId", key), ("UserId", user));
        if (tenant.IsHost)
        {
            if (principal.HasClaim(claim => claim.Type == IdentityClaimTypes.TenantId)
                || principal.FindFirst(IdentityClaimTypes.Scope)?.Value != "host"
                || principal.FindFirst(IdentityClaimTypes.ActorScope)?.Value != "host") return null;
        }
        else
        {
            if (!tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty
                || !Guid.TryParse(principal.FindFirst(IdentityClaimTypes.TenantId)?.Value, out var claimedTenant) || claimedTenant != tenantId
                || principal.FindFirst(IdentityClaimTypes.Scope)?.Value != $"tenant:{tenantId:N}"
                || principal.FindFirst(IdentityClaimTypes.ActorScope)?.Value != "tenant") return null;
            parameters.Add("TenantScopeKey", $"tenant:{tenantId:N}");
        }
        var row = await query.QuerySingleOrDefaultAsync<ApiKeyAuthenticationRow>(tenant.IsHost
            ? ApiKeySql.AuthorizeCurrentHost : ApiKeySql.AuthorizeCurrentTenant, parameters, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (row is null || !row.IsActive || !row.UserIsActive || row.UserLockoutEndUtc > now || row.ExpiresAtUtc <= now
            || !string.Equals(row.SecurityStamp, principal.FindFirst(IdentityClaimTypes.SecurityStamp)?.Value, StringComparison.Ordinal)) return null;
        var activePermissions = ApiKeyAuthenticationService.DeserializePermissions(row.PermissionsJson);
        return permissions.All(permission => activePermissions.Contains(permission, StringComparer.Ordinal)) ? user : null;
    }
}
