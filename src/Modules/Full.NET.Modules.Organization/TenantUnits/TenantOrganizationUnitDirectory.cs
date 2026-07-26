using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>
/// 为其他模块提供租户机构单元存在性只读校验。
/// </summary>
internal sealed class TenantOrganizationUnitDirectory(IQueryExecutor queryExecutor)
    : ITenantOrganizationUnitDirectory,
      IIdentityOrganizationUnitDirectory
{
    async Task<TenantOrganizationUnitDirectoryEntry?>
        ITenantOrganizationUnitDirectory.FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(tenantId, unitId, cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? null
            : new TenantOrganizationUnitDirectoryEntry(record.Id, record.Code, record.Name);
    }

    async Task<IdentityOrganizationUnitDirectoryEntry?>
        IIdentityOrganizationUnitDirectory.FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(tenantId, unitId, cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? null
            : new IdentityOrganizationUnitDirectoryEntry(record.Id, record.Code, record.Name);
    }

    private async Task<OrganizationUnitRecord?> FindRecordAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindActiveUnitByTenantAndId,
                new { UnitId = unitId, TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null || !record.IsActive ? null : record;
    }
}
