using Full.NET.Abstractions.Results;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>通过 Organization 批量目录回填 Identity 机构单元投影。</summary>
internal sealed class OrganizationUnitProjectionBackfillService(
    IOrganizationUnitProjectionCatalog catalog,
    OrganizationUnitProjectionWriter writer)
{
    public async Task<Result<OrganizationUnitProjectionBackfillResult>> BackfillTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        var page = 1;
        var applied = 0L;
        while (true)
        {
            var pageResult = await catalog.ListUnitSnapshotsAsync(
                    tenantId,
                    page,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!pageResult.IsSuccess)
            {
                return Result<OrganizationUnitProjectionBackfillResult>.Failure(pageResult.Error!);
            }

            var batch = pageResult.Value!;
            if (batch.Items.Count == 0)
            {
                break;
            }

            foreach (var snapshot in batch.Items)
            {
                await writer.ApplySnapshotAsync(tenantId, snapshot, cancellationToken)
                    .ConfigureAwait(false);
                applied++;
            }

            if (batch.Items.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return Result<OrganizationUnitProjectionBackfillResult>.Success(
            new OrganizationUnitProjectionBackfillResult(tenantId, applied));
    }
}

/// <summary>单租户投影回填结果。</summary>
public sealed record OrganizationUnitProjectionBackfillResult(Guid TenantId, long AppliedCount);
