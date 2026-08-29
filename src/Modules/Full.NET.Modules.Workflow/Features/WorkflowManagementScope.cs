using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Features;

/// <summary>将可信租户上下文收敛为数据库唯一键，禁止管理请求自行声明作用域。</summary>
internal readonly record struct WorkflowManagementScope(
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey)
{
    public static WorkflowManagementScope Resolve(ICurrentTenant currentTenant)
    {
        if (currentTenant.IsHost)
        {
            return new(null, "host", "host");
        }

        if (currentTenant.IsAvailable && currentTenant.Id is { } tenantId)
        {
            return new(tenantId, "tenant", $"tenant:{tenantId:N}");
        }

        throw new TenantContextMissingException("workflow.tenant_context_required");
    }
}
