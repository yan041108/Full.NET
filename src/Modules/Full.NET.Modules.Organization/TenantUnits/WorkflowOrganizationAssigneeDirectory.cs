using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>从 Organization 权威隶属与显式领导权关系中解析 Workflow 组织来源。</summary>
/// <param name="queryExecutor">受租户作用域保护的只读查询执行器。</param>
/// <param name="databaseOptions">当前数据库提供程序配置。</param>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
/// <param name="transactionState">阻止消费方在本地事务内调用组织目录。</param>
internal sealed partial class WorkflowOrganizationAssigneeDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant,
    IDataTransactionState transactionState) : IWorkflowOrganizationAssigneeDirectory
{
    /// <inheritdoc />
    public async Task<bool> IsActiveMemberOfAnyPositionAsync(Guid userId, IReadOnlyCollection<Guid> positionIds, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(); var ids = NormalizeIds(positionIds, 5);
        if (userId == Guid.Empty || ids.Length == 0) return false;
        return await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowOrganizationAssigneeSql.IsPositionMember,
            OrganizationSqlParameters.Create(("UserId", userId), ("PositionIds", ids)), cancellationToken).ConfigureAwait(false) > 0;
    }
    /// <inheritdoc />
    public async Task<bool> IsActiveMemberOfAnyUnitAsync(Guid userId, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(); var ids = NormalizeIds(unitIds, 8);
        if (userId == Guid.Empty || ids.Length == 0) return false;
        return await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowOrganizationAssigneeSql.IsUnitMember,
            OrganizationSqlParameters.Create(("UserId", userId), ("UnitIds", ids)), cancellationToken).ConfigureAwait(false) > 0;
    }
    /// <summary>按稳定顺序分页返回当前租户可用岗位，偏移使用长整型防止溢出。</summary>
    public async Task<PagedResult<WorkflowOrganizationPositionDirectoryEntry>> ListActivePositionsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = OrganizationSqlParameters.Create(("Offset", (long)(page-1)*pageSize), ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(WorkflowOrganizationAssigneeSql.CountActivePositions, parameters, cancellationToken).ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch {
            DatabaseProvider.SqlServer => WorkflowOrganizationAssigneeSql.ListActivePositionsSqlServer,
            DatabaseProvider.MySql => WorkflowOrganizationAssigneeSql.ListActivePositionsMySql,
            _ => throw new InvalidOperationException("Unsupported organization database provider."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowOrganizationPositionListRow>(statement, parameters, cancellationToken).ConfigureAwait(false);
        return new PagedResult<WorkflowOrganizationPositionDirectoryEntry>(rows.Select(MapPosition).ToArray(), page, pageSize, total);
    }

    /// <summary>活动岗位不存在或所属机构已停用时不返回候选。</summary>
    public async Task<IReadOnlyDictionary<Guid, WorkflowOrganizationPositionDirectoryEntry>> FindActivePositionsAsync(
        IReadOnlyCollection<Guid> positionIds, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var ids = NormalizeIds(positionIds, 5);
        if (ids.Length == 0) return new Dictionary<Guid, WorkflowOrganizationPositionDirectoryEntry>();
        var rows = await queryExecutor.QueryAsync<WorkflowOrganizationPositionListRow>(WorkflowOrganizationAssigneeSql.FindActivePositionsByIds,
            OrganizationSqlParameters.Create(("PositionIds", ids)), cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, MapPosition);
    }

    /// <summary>先确定活动岗位集合，再一次批量读取全部成员，保留有效空岗位。</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActivePositionMemberUserIdsAsync(
        IReadOnlyCollection<Guid> positionIds, int maximumMembersPerPosition, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(); ValidateMemberLimit(maximumMembersPerPosition);
        var ids = NormalizeIds(positionIds, 5);
        var positions = await FindActivePositionsAsync(ids, cancellationToken).ConfigureAwait(false);
        if (positions.Count == 0) return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var rows = await queryExecutor.QueryAsync<WorkflowOrganizationAssigneeMemberRow>(WorkflowOrganizationAssigneeSql.FindPositionMembers,
            OrganizationSqlParameters.Create(("PositionIds", positions.Keys.ToArray()), ("Take", maximumMembersPerPosition)), cancellationToken).ConfigureAwait(false);
        return MapMembers(positions.Keys, rows);
    }

    /// <summary>机构成员只依赖 Organization 自有隶属事实，活动账号验证仍由 Identity 完成。</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActiveUnitMemberUserIdsAsync(
        IReadOnlyCollection<Guid> unitIds, int maximumMembersPerUnit, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext(); ValidateMemberLimit(maximumMembersPerUnit);
        var ids = NormalizeIds(unitIds, 8);
        var units = await FindActiveUnitsAsync(ids, cancellationToken).ConfigureAwait(false);
        if (units.Count == 0) return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var rows = await queryExecutor.QueryAsync<WorkflowOrganizationAssigneeMemberRow>(WorkflowOrganizationAssigneeSql.FindUnitMembers,
            OrganizationSqlParameters.Create(("UnitIds", units.Keys.ToArray()), ("Take", maximumMembersPerUnit)), cancellationToken).ConfigureAwait(false);
        return MapMembers(units.Keys, rows);
    }

    private static WorkflowOrganizationPositionDirectoryEntry MapPosition(WorkflowOrganizationPositionListRow row) => new(row.Id, row.Code, row.Name);

    /// <summary>来源无成员是明确的空事实；不以其他来源或发起人代替。</summary>
    private static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> MapMembers(IEnumerable<Guid> sourceIds, IReadOnlyList<WorkflowOrganizationAssigneeMemberRow> rows)
    {
        var values = sourceIds.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var row in rows) values[row.SourceId].Add(row.UserId);
        return values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Guid>)pair.Value);
    }

    private static Guid[] NormalizeIds(IReadOnlyCollection<Guid> values, int maximum)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ids = values.Distinct().ToArray();
        if (ids.Length > maximum || ids.Contains(Guid.Empty)) throw new ArgumentException("Organization selection is outside its bounds.", nameof(values));
        return ids;
    }

    private static void ValidateMemberLimit(int value)
    {
        if (value is < 1 or > 21) throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>分页读取当前可信租户内可配置为办理人来源的活动机构单元。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动机构单元候选分页结果。</returns>
    public async Task<PagedResult<WorkflowOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = OrganizationSqlParameters.Create(
            ("Offset", (long)(page - 1) * pageSize),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowOrganizationAssigneeSql.CountActiveUnits,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowOrganizationAssigneeSql.ListActiveUnitsSqlServer,
            DatabaseProvider.MySql => WorkflowOrganizationAssigneeSql.ListActiveUnitsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowOrganizationUnitListRow>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<WorkflowOrganizationUnitDirectoryEntry>(
            rows.Select(Map).ToArray(),
            page,
            pageSize,
            total);
    }

    /// <summary>批量校验机构单元是否仍处于活动状态且属于当前可信租户。</summary>
    /// <param name="unitIds">待校验的机构单元标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>有效机构单元的目录项。</returns>
    public async Task<IReadOnlyDictionary<Guid, WorkflowOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        EnsureTenantContext();
        var distinctUnitIds = unitIds.Distinct().ToArray();
        if (distinctUnitIds.Length == 0)
        {
            return new Dictionary<Guid, WorkflowOrganizationUnitDirectoryEntry>();
        }

        var rows = await queryExecutor.QueryAsync<WorkflowOrganizationUnitListRow>(
                WorkflowOrganizationAssigneeSql.FindActiveUnitsByIds,
                OrganizationSqlParameters.Create(("UnitIds", distinctUnitIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, Map);
    }

    /// <summary>将数据库投影映射为 Workflow Contract 最小机构单元条目。</summary>
    /// <param name="row">机构单元列表行。</param>
    /// <returns>机构单元目录项。</returns>
    private static WorkflowOrganizationUnitDirectoryEntry Map(WorkflowOrganizationUnitListRow row) =>
        new(row.Id, row.Code, row.Name);

    /// <summary>确认当前请求处于有效 Tenant 上下文。</summary>
    /// <exception cref="TenantContextMissingException">Host 或其他无效上下文时抛出。</exception>
    private void EnsureTenantContext()
    {
        if (transactionState.HasTransaction)
            throw new InvalidOperationException("Organization workflow directory reads must run outside a module transaction.");
        if (currentTenant.IsHost ||
            !currentTenant.IsAvailable ||
            currentTenant.Id is null)
        {
            throw new TenantContextMissingException("organization.tenant_context_required");
        }
    }
}
