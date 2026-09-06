using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>
/// 为其他模块提供租户职位存在性只读校验。
/// </summary>
internal sealed class OrganizationPositionDirectory(IQueryExecutor queryExecutor)
    : IIdentityOrganizationPositionDirectory
{
    /// <inheritdoc />
    public async Task<IdentityOrganizationPositionDirectoryEntry?> FindActivePositionAsync(
        Guid tenantId,
        Guid positionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindActiveByTenantAndId,
                OrganizationSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("PositionId", positionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null || !record.IsActive
            ? null
            : new IdentityOrganizationPositionDirectoryEntry(
                record.Id,
                record.Code,
                record.Name);
    }
}
