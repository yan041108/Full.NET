using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>从 Identity 本地投影表读取活动机构单元。</summary>
internal sealed class OrganizationUnitProjectionDirectory(IQueryExecutor queryExecutor)
    : IOrganizationUnitProjectionDirectory
{
    public async Task<IReadOnlyDictionary<Guid, OrganizationUnitProjectionEntry>> FindActiveUnitsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default)
    {
        if (unitIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(unitIds));
        if (unitIds.Count == 0) return new Dictionary<Guid, OrganizationUnitProjectionEntry>();
        var rows = await queryExecutor.QueryAsync<OrganizationUnitProjectionRecord>(
            OrganizationUnitProjectionSql.FindByTenantAndUnits,
            IdentitySqlParameters.Create(("TenantId", tenantId), ("UnitIds", unitIds.Distinct().ToArray())),
            cancellationToken).ConfigureAwait(false);
        return rows.Where(row => row.IsActive).ToDictionary(row => row.UnitId,
            row => new OrganizationUnitProjectionEntry(row.UnitId, row.Name));
    }

    public async Task<OrganizationUnitProjectionEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitProjectionRecord>(
                OrganizationUnitProjectionSql.FindActiveByTenantAndUnit,
                IdentitySqlParameters.Create(("TenantId", tenantId), ("UnitId", unitId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? null
            : new OrganizationUnitProjectionEntry(record.UnitId, record.Name);
    }
}
