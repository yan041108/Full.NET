using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Features;

/// <summary>将可信租户上下文收敛为站内信作用域键，禁止请求体自行声明 TenantId。</summary>
internal readonly record struct NotificationInboxScope(
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey)
{
    public bool IsHost => string.Equals(ScopeKey, "host", StringComparison.Ordinal);

    public static NotificationInboxScope Resolve(ICurrentTenant currentTenant)
    {
        if (currentTenant.IsHost)
        {
            return new(null, "host", "host");
        }

        if (currentTenant.IsAvailable && currentTenant.Id is { } tenantId)
        {
            return new(tenantId, "tenant", $"tenant:{tenantId:N}");
        }

        throw new TenantContextMissingException("notifications.tenant_context_required");
    }
}
