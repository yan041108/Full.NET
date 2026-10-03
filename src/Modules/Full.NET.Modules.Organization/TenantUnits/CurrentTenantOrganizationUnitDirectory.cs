using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>逐次查询机构权威状态，不借用跨租户成员目录或缓存回答。</summary>
/// <param name="query">带租户守卫的只读执行器。</param>
/// <param name="database">成对分页语法的数据库提供程序。</param>
/// <param name="tenant">认证管线建立的可信当前租户。</param>
internal sealed class CurrentTenantOrganizationUnitDirectory(IQueryExecutor query, IOptions<DatabaseOptions> database, ICurrentTenant tenant)
    : ICurrentTenantOrganizationUnitDirectory
{
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, TenantOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(
        IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        if (unitIds.Count > 100 || unitIds.Contains(Guid.Empty) || unitIds.Distinct().Count() != unitIds.Count)
            throw new ArgumentOutOfRangeException(nameof(unitIds));
        RequireTenant();
        if (unitIds.Count == 0) return new Dictionary<Guid, TenantOrganizationUnitDirectoryEntry>();
        var rows = await query.QueryAsync<CurrentTenantOrganizationUnitDirectoryRow>(CurrentTenantOrganizationUnitDirectorySql.Find,
            OrganizationSqlParameters.Create(("UnitIds", unitIds.ToArray())), cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new TenantOrganizationUnitDirectoryEntry(row.Id, row.Code, row.Name));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> ListActiveUnitIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireTenant();
        if (userId == Guid.Empty) return [];
        return await query.QueryAsync<Guid>(CurrentTenantOrganizationUnitDirectorySql.UserUnits,
            OrganizationSqlParameters.Create(("UserId", userId)), cancellationToken).ConfigureAwait(false);
    }

    private void RequireTenant()
    {
        if (tenant.IsHost || !tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty)
            throw new TenantContextMissingException("organization.tenant_context_required");
    }

    /// <inheritdoc/>
    public async Task<PagedResult<TenantOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || offset > 100_000)
            throw new ArgumentOutOfRangeException(nameof(page));
        // 此 Port 只有租户目录；Host 及不完整上下文均不能降级为跨租户目录。
        RequireTenant();
        var parameters = OrganizationSqlParameters.Create(("Offset", offset), ("PageSize", pageSize));
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => CurrentTenantOrganizationUnitDirectorySql.ListSqlServer,
            DatabaseProvider.MySql => CurrentTenantOrganizationUnitDirectorySql.ListMySql,
            _ => throw new InvalidOperationException("Unsupported organization directory database provider."),
        };
        var total = await query.QuerySingleOrDefaultAsync<long>(CurrentTenantOrganizationUnitDirectorySql.Count, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await query.QueryAsync<CurrentTenantOrganizationUnitDirectoryRow>(statement, parameters, cancellationToken).ConfigureAwait(false);
        return new(rows.Select(row => new TenantOrganizationUnitDirectoryEntry(row.Id, row.Code, row.Name)).ToArray(), page, pageSize, total);
    }
}
