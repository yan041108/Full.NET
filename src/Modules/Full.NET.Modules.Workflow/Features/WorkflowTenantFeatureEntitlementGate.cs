using Full.NET.Abstractions.Results;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Workflow.Features;

/// <summary>Enforced 阶段下，租户作用域 Workflow 变更须具备 <c>feature.workflow</c> 绑定。</summary>
internal static class WorkflowTenantFeatureEntitlementGate
{
    /// <summary>Host 作用域始终放行；租户作用域按权益 Port 评估。</summary>
    public static async Task<Error?> TryGetDenialAsync(
        WorkflowManagementScope scope,
        ITenantFeatureEntitlementPort featureEntitlements,
        CancellationToken cancellationToken)
    {
        if (scope.TenantId is not Guid tenantId)
        {
            return null;
        }

        var entitled = await featureEntitlements
            .IsFeatureGrantedAsync(tenantId, TenantEntitlementCatalogCodes.Workflow, cancellationToken)
            .ConfigureAwait(false);
        return entitled.IsSuccess ? null : entitled.Error;
    }

    public static Result<T> Deny<T>(Error error) => Result<T>.Failure(error);
}
