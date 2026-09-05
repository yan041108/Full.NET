using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>从 Organization 权威机构隶属与职级关系中解析 Workflow 办理人负责人候选。</summary>
/// <param name="queryExecutor">受租户作用域保护的只读查询执行器。</param>
/// <param name="databaseOptions">当前数据库提供程序配置。</param>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
internal sealed class WorkflowUnitLeaderDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant) : IWorkflowUnitLeaderDirectory
{
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
            ("Offset", (page - 1) * pageSize),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowUnitLeaderSql.CountActiveUnits,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowUnitLeaderSql.ListActiveUnitsSqlServer,
            DatabaseProvider.MySql => WorkflowUnitLeaderSql.ListActiveUnitsMySql,
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
                WorkflowUnitLeaderSql.FindActiveUnitsByIds,
                OrganizationSqlParameters.Create(("UnitIds", distinctUnitIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, Map);
    }

    /// <summary>按机构单元批量解析当前租户内职级最高的负责人用户标识。</summary>
    /// <param name="unitIds">待解析的机构单元标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以机构单元标识为键、负责人用户标识为值。</returns>
    public async Task<IReadOnlyDictionary<Guid, Guid>> FindActiveUnitLeaderUserIdsAsync(
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        EnsureTenantContext();
        var distinctUnitIds = unitIds.Distinct().ToArray();
        if (distinctUnitIds.Length == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowUnitLeaderSql.ListUnitLeaderCandidatesSqlServer,
            DatabaseProvider.MySql => WorkflowUnitLeaderSql.ListUnitLeaderCandidatesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<WorkflowUnitLeaderRow>(
                statement,
                OrganizationSqlParameters.Create(("UnitIds", distinctUnitIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.UnitId, row => row.UserId);
    }

    /// <summary>解析发起人主部门在当前租户内的负责人用户标识。</summary>
    /// <param name="initiatorUserId">工作流实例发起人标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>负责人用户标识；发起人无主部门或负责人不存在时返回 <see langword="null"/>。</returns>
    public async Task<Guid?> FindInitiatorPrimaryUnitLeaderUserIdAsync(
        Guid initiatorUserId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var primaryUnitId = await queryExecutor.QuerySingleOrDefaultAsync<Guid?>(
                WorkflowUnitLeaderSql.FindInitiatorPrimaryUnitId,
                OrganizationSqlParameters.Create(("UserId", initiatorUserId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (primaryUnitId is not { } unitId)
        {
            return null;
        }

        var leaders = await FindActiveUnitLeaderUserIdsAsync([unitId], cancellationToken)
            .ConfigureAwait(false);
        return leaders.TryGetValue(unitId, out var leaderUserId) ? leaderUserId : null;
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
        if (currentTenant.IsHost ||
            !currentTenant.IsAvailable ||
            currentTenant.Id is null)
        {
            throw new TenantContextMissingException("organization.tenant_context_required");
        }
    }
}
