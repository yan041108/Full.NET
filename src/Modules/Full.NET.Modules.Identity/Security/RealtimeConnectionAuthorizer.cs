using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Realtime;

namespace Full.NET.Modules.Identity.Security;

/// <summary>复用 HTTP 的权威会话验证，并独立确认租户有效性和令牌期限。</summary>
internal sealed class RealtimeConnectionAuthorizer(AccessSessionValidator sessions,
    IActiveTenantContextResolver tenants, ICurrentTenantContextWriter context, IClock clock) : IRealtimeConnectionAuthorizer
{
    public async Task<bool> IsAuthorizedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!long.TryParse(principal.FindFirstValue("exp"), out var expiry)
            || expiry <= clock.UtcNow.ToUnixTimeSeconds()) return false;
        context.SetHost();
        try
        {
            if (!await sessions.IsValidAsync(principal, cancellationToken).ConfigureAwait(false)) return false;
            var claim = principal.FindFirstValue("fullnet_tenant_id");
            return claim is null || Guid.TryParse(claim, out var tenantId)
                && await tenants.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is not null;
        }
        finally { context.Clear(); }
    }
}
