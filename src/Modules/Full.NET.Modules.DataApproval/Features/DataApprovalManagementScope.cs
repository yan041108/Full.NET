using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.DataApproval.Features;

/// <summary>将可信租户上下文收敛为 DataApproval 数据库作用域键。</summary>
internal readonly record struct DataApprovalManagementScope(
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey)
{
    /// <summary>解析当前请求的 Host 或租户作用域。</summary>
    /// <param name="currentTenant">可信租户上下文。</param>
    public static DataApprovalManagementScope Resolve(ICurrentTenant currentTenant)
    {
        if (currentTenant.IsHost)
        {
            return new(null, "host", "host");
        }

        if (currentTenant.IsAvailable && currentTenant.Id is { } tenantId)
        {
            return new(tenantId, "tenant", $"tenant:{tenantId:N}");
        }

        throw new TenantContextMissingException("data_approvals.tenant_context_required");
    }
}
