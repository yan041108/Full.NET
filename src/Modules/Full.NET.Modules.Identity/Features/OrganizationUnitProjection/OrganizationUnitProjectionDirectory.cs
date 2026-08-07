using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>从 Identity 本地投影表读取活动机构单元。</summary>
internal sealed class OrganizationUnitProjectionDirectory(IQueryExecutor queryExecutor)
    : IOrganizationUnitProjectionDirectory
{
    public async Task<OrganizationUnitProjectionEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitProjectionRecord>(
                OrganizationUnitProjectionSql.FindActiveByTenantAndUnit,
                new { TenantId = tenantId, UnitId = unitId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? null
            : new OrganizationUnitProjectionEntry(record.UnitId, record.Name);
    }
}
