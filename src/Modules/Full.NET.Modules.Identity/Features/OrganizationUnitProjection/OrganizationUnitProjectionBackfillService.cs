using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>通过有界对账分页回填 Identity 机构单元投影。</summary>
internal sealed class OrganizationUnitProjectionBackfillService(
    OrganizationUnitProjectionReconciliationService reconciliation)
{
    public async Task<Result<OrganizationUnitProjectionBackfillResult>> BackfillTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        Guid? afterUnitId = null;
        var applied = 0L;
        while (true)
        {
            var pageResult = await reconciliation.ReconcileAsync(
                    tenantId,
                    afterUnitId,
                    pageSize,
                    IdentityOrganizationUnitProjectionReconciliationModes.Apply,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!pageResult.IsSuccess)
            {
                return Result<OrganizationUnitProjectionBackfillResult>.Failure(
                    pageResult.Error!);
            }

            var batch = pageResult.Value!;
            applied += batch.Applied;
            if (batch.IsComplete)
            {
                break;
            }

            afterUnitId = batch.NextAfterUnitId;
        }

        return Result<OrganizationUnitProjectionBackfillResult>.Success(
            new OrganizationUnitProjectionBackfillResult(tenantId, applied));
    }
}

/// <summary>单租户投影回填结果。</summary>
public sealed record OrganizationUnitProjectionBackfillResult(Guid TenantId, long AppliedCount);
