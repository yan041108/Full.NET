using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Realtime;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

/// <summary>复用权威会话验证，独立确认租户、当前成员准入与令牌期限；拒绝后由连接租约关闭原连接。</summary>
internal sealed class RealtimeConnectionAuthorizer(AccessSessionValidator sessions,
    IActiveTenantContextResolver tenants, ICurrentTenantContextWriter context, IClock clock,
    IdentityTenantResourceAccess access) : IRealtimeConnectionAuthorizer
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
            Guid? tenantId = null;
            if (claim is not null)
            {
                if (!Guid.TryParse(claim, out var id)
                    || await tenants.ResolveActiveByIdAsync(id, cancellationToken).ConfigureAwait(false) is null) return false;
                tenantId = id;
            }
            if (!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out var userId)) return false;
            return await access.IsActorAllowedAsync(userId,
                principal.FindFirstValue(FullNetIdentityClaimTypes.ActorScope) ?? "", tenantId, cancellationToken).ConfigureAwait(false)
                && expiry > clock.UtcNow.ToUnixTimeSeconds();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        // 连接轮询遇到权威读取失败时拒绝原连接，不保留上次活动成员结果。
        catch (Exception) { return false; }
        finally { context.Clear(); }
    }
}
