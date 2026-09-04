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

    /// <summary>从 Integration Event Envelope 的可信租户标识创建通知作用域。</summary>
    /// <param name="tenantId">可信消息租户标识；为空表示 Host 作用域。</param>
    /// <returns>可用于 Notifications SQL 过滤的稳定作用域。</returns>
    public static NotificationInboxScope FromTrustedTenantId(Guid? tenantId) =>
        tenantId is { } value
            ? new(value, "tenant", $"tenant:{value:N}")
            : new(null, "host", "host");

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
