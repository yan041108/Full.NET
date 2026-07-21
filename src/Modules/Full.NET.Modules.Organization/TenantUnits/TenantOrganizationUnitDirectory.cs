using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>
/// 为其他模块提供租户机构单元存在性只读校验。
/// </summary>
internal sealed class TenantOrganizationUnitDirectory(IQueryExecutor queryExecutor)
    : ITenantOrganizationUnitDirectory
{
    public async Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindActiveUnitByTenantAndId,
                new { UnitId = unitId, TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null || !record.IsActive
            ? null
            : new TenantOrganizationUnitDirectoryEntry(record.Id, record.Code, record.Name);
    }
}
