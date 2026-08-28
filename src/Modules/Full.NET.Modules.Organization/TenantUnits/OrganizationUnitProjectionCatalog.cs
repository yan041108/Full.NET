using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>为 Identity 回填提供机构单元批量只读目录。</summary>
internal sealed class OrganizationUnitProjectionCatalog(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IIdentityOrganizationUnitProjectionSource
{
    private static readonly Guid UnusedAfterUnitId =
        Guid.Parse("00000000-0000-7000-8000-000000000000");

    public async Task<Result<IdentityOrganizationUnitProjectionPage>> ListAsync(
        Guid tenantId,
        Guid? afterUnitId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var fetchSize = pageSize + 1;
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OrganizationSql.ListUnitSnapshotsKeysetSqlServer,
            DatabaseProvider.MySql => OrganizationSql.ListUnitSnapshotsKeysetMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<OrganizationUnitSnapshotRow>(
                listStatement,
                OrganizationSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("HasAfterUnitId", afterUnitId.HasValue ? 1 : 0),
                    ("AfterUnitId", afterUnitId ?? UnusedAfterUnitId),
                    ("PageSize", fetchSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var hasMore = rows.Count > pageSize;
        var visibleRows = hasMore ? rows.Take(pageSize) : rows;
        var items = visibleRows
            .Select(row => new IdentityOrganizationUnitProjectionSnapshot(
                row.UnitId,
                row.Name,
                row.IsActive,
                row.Version,
                row.ChangedAtUtc))
            .ToArray();
        var nextAfterUnitId = hasMore ? items[^1].UnitId : (Guid?)null;
        return Result<IdentityOrganizationUnitProjectionPage>.Success(
            new IdentityOrganizationUnitProjectionPage(items, nextAfterUnitId, hasMore));
    }
}