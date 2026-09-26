using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

/// <summary>租户成员写操作前校验当前租户仍处于活动状态（暂停租户 IsActive=false）。</summary>
internal static class TenantMembershipActiveTenantGuard
{
    internal static async Task<Result<bool>> EnsureCurrentTenantActiveAsync(
        ICurrentTenant currentTenant,
        IIdentityActiveTenantDirectory activeTenants,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.DataScopeTenantContextRequired,
                "Tenant context is required.",
                ErrorType.Validation));
        }

        return await EnsureTenantActiveAsync(tenantId, activeTenants, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Result<bool>> EnsureTenantActiveAsync(
        Guid tenantId,
        IIdentityActiveTenantDirectory activeTenants,
        CancellationToken cancellationToken)
    {
        if (!await activeTenants.IsActiveTenantAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.TenantMembershipTenantInactive,
                "The tenant is suspended or inactive.",
                ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }
}
