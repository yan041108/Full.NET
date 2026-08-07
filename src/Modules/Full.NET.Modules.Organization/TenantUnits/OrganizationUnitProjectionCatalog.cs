using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>为 Identity 回填提供机构单元批量只读目录。</summary>
internal sealed class OrganizationUnitProjectionCatalog(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IOrganizationUnitProjectionCatalog
{
    public async Task<Result<PagedResult<OrganizationUnitProjectionSnapshot>>> ListUnitSnapshotsAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                OrganizationSql.CountUnitSnapshotsForTenant,
                new { TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OrganizationSql.ListUnitSnapshotsForTenantSqlServer,
            DatabaseProvider.MySql => OrganizationSql.ListUnitSnapshotsForTenantMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<OrganizationUnitSnapshotRow>(
                listStatement,
                new
                {
                    TenantId = tenantId,
                    Offset = offset,
                    PageSize = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Select(row => new OrganizationUnitProjectionSnapshot(
                row.UnitId,
                row.Name,
                row.IsActive,
                row.Version,
                row.ChangedAtUtc))
            .ToArray();
        return Result<PagedResult<OrganizationUnitProjectionSnapshot>>.Success(
            new PagedResult<OrganizationUnitProjectionSnapshot>(items, page, pageSize, total));
    }
}
