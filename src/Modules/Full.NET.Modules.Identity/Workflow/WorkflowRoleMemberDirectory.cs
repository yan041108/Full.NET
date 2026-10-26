using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Workflow;

/// <summary>从 Identity 权威角色与成员关系中解析 Workflow 可信办理人候选。</summary>
/// <param name="queryExecutor">受数据作用域保护的只读查询执行器。</param>
/// <param name="databaseOptions">当前数据库提供程序配置。</param>
/// <param name="currentTenant">由认证与租户中间件建立的可信当前租户。</param>
internal sealed class WorkflowRoleMemberDirectory(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    ICurrentTenant currentTenant) : IWorkflowRoleMemberDirectory
{
    /// <inheritdoc />
    public async Task<PagedResult<WorkflowRoleUserDirectoryEntry>> ListActiveMembersOfRolesAsync(IReadOnlyCollection<Guid> roleIds,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        var roles = roleIds.Distinct().ToArray();
        if (roles.Length is < 1 or > 5 || roles.Contains(Guid.Empty) || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Workflow role selection is outside its bounds.");
        var parameters = IdentitySqlParameters.Create(("RoleIds", roles), ("Offset", (long)(page - 1) * pageSize), ("PageSize", pageSize));
        var mysql = databaseOptions.Value.Provider == DatabaseProvider.MySql;
        var count = WorkflowRoleSelectionSql.CountHost;
        var list = mysql ? WorkflowRoleSelectionSql.ListHostMySql : WorkflowRoleSelectionSql.ListHostSqlServer;
        if (!currentTenant.IsHost)
        {
            var scope = ResolveTenantScope(); parameters["TenantId"] = scope.TenantId; parameters["TenantScopeKey"] = scope.TenantScopeKey;
            count = WorkflowRoleSelectionSql.CountTenant; list = mysql ? WorkflowRoleSelectionSql.ListTenantMySql : WorkflowRoleSelectionSql.ListTenantSqlServer;
        }
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(count, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<WorkflowRoleUserRow>(list, parameters, cancellationToken).ConfigureAwait(false);
        return new(rows.Select(row => new WorkflowRoleUserDirectoryEntry(row.Id, row.Username, row.DisplayName)).ToArray(), page, pageSize, total);
    }
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindSelectedActiveMembersByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds); ArgumentNullException.ThrowIfNull(userIds);
        var roles = roleIds.Distinct().ToArray(); var users = userIds.Distinct().ToArray();
        if (roles.Length > 320 || users.Length > 160 || roles.Contains(Guid.Empty) || users.Contains(Guid.Empty))
            throw new ArgumentException("Workflow selection is outside its bounds.");
        if (roles.Length == 0 || users.Length == 0) return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var statement = WorkflowRoleMemberSql.FindSelectedHostRoleMembers;
        var parameters = IdentitySqlParameters.Create(("RoleIds", roles), ("UserIds", users));
        if (!currentTenant.IsHost)
        {
            var scope = ResolveTenantScope(); parameters["TenantId"] = scope.TenantId; parameters["TenantScopeKey"] = scope.TenantScopeKey;
            statement = WorkflowRoleMemberSql.FindSelectedTenantRoleMembers;
        }
        var rows = await queryExecutor.QueryAsync<WorkflowRoleMemberRow>(statement, parameters, cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(row => row.RoleId).ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(row => row.UserId).ToArray());
    }
    /// <inheritdoc />
    public async Task<bool> IsActiveMemberOfAnyRoleAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        var ids = roleIds.Distinct().ToArray();
        if (ids.Length > 5 || ids.Contains(Guid.Empty)) throw new ArgumentException("Workflow role source is outside its bounds.", nameof(roleIds));
        if (userId == Guid.Empty || ids.Length == 0) return false;
        var parameters = IdentitySqlParameters.Create(("UserId", userId), ("RoleIds", ids));
        var statement = WorkflowRoleMemberSql.IsActiveHostRoleMember;
        if (!currentTenant.IsHost)
        {
            var scope = ResolveTenantScope(); parameters["TenantId"] = scope.TenantId; parameters["TenantScopeKey"] = scope.TenantScopeKey;
            statement = WorkflowRoleMemberSql.IsActiveTenantRoleMember;
        }
        return await queryExecutor.QuerySingleOrDefaultAsync<int>(statement, parameters, cancellationToken).ConfigureAwait(false) == 1;
    }
    /// <summary>分页读取当前可信作用域内可配置为办理人来源的活动角色。</summary>
    /// <param name="page">从 1 开始的页码。</param>
    /// <param name="pageSize">受控单页数量。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动角色候选分页结果。</returns>
    public async Task<PagedResult<WorkflowRoleDirectoryEntry>> ListActiveRolesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (currentTenant.IsHost)
        {
            var parameters = IdentitySqlParameters.Create(
                ("Offset", (page - 1) * pageSize),
                ("PageSize", pageSize));
            var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    WorkflowRoleMemberSql.CountActiveHostRoles,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            var statement = databaseOptions.Value.Provider switch
            {
                DatabaseProvider.SqlServer => WorkflowRoleMemberSql.ListActiveHostRolesSqlServer,
                DatabaseProvider.MySql => WorkflowRoleMemberSql.ListActiveHostRolesMySql,
                _ => throw new InvalidOperationException(
                    "The configured database provider is not supported."),
            };
            var rows = await queryExecutor.QueryAsync<WorkflowRoleListRow>(
                    statement,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PagedResult<WorkflowRoleDirectoryEntry>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total);
        }

        var scope = ResolveTenantScope();
        var tenantParameters = IdentitySqlParameters.Create(
            ("TenantId", scope.TenantId),
            ("TenantScopeKey", scope.TenantScopeKey),
            ("Offset", (page - 1) * pageSize),
            ("PageSize", pageSize));
        var tenantTotal = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowRoleMemberSql.CountActiveTenantRoles,
                tenantParameters,
                cancellationToken)
            .ConfigureAwait(false);
        var tenantStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowRoleMemberSql.ListActiveTenantRolesSqlServer,
            DatabaseProvider.MySql => WorkflowRoleMemberSql.ListActiveTenantRolesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var tenantRows = await queryExecutor.QueryAsync<WorkflowRoleListRow>(
                tenantStatement,
                tenantParameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<WorkflowRoleDirectoryEntry>(
            tenantRows.Select(Map).ToArray(),
            page,
            pageSize,
            tenantTotal);
    }

    /// <summary>批量校验角色是否仍处于活动状态且属于当前可信作用域。</summary>
    /// <param name="roleIds">待校验的角色标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>有效角色的目录项。</returns>
    public async Task<IReadOnlyDictionary<Guid, WorkflowRoleDirectoryEntry>> FindActiveRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        var distinctRoleIds = roleIds.Distinct().ToArray();
        if (distinctRoleIds.Length == 0)
        {
            return new Dictionary<Guid, WorkflowRoleDirectoryEntry>();
        }

        if (currentTenant.IsHost)
        {
            var rows = await queryExecutor.QueryAsync<WorkflowRoleListRow>(
                    WorkflowRoleMemberSql.FindActiveHostRolesByIds,
                    IdentitySqlParameters.Create(("RoleIds", distinctRoleIds)),
                    cancellationToken)
                .ConfigureAwait(false);
            return rows.ToDictionary(row => row.Id, Map);
        }

        var scope = ResolveTenantScope();
        var tenantRows = await queryExecutor.QueryAsync<WorkflowRoleListRow>(
                WorkflowRoleMemberSql.FindActiveTenantRolesByIds,
                IdentitySqlParameters.Create(
                    ("TenantId", scope.TenantId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("RoleIds", distinctRoleIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return tenantRows.ToDictionary(row => row.Id, Map);
    }

    /// <summary>按角色批量解析当前可信作用域内的活动成员用户标识。</summary>
    /// <param name="roleIds">待解析的角色标识集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>以角色标识为键、去重且稳定排序后的活动用户标识列表为值。</returns>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FindActiveMemberUserIdsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds, int maximumMembersPerRole, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMembersPerRole, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumMembersPerRole, 21);
        if (roleIds.Count > 5) throw new ArgumentException("Workflow role source cannot exceed five roles.", nameof(roleIds));
        var result = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var activeRoles = await FindActiveRolesAsync(roleIds, cancellationToken).ConfigureAwait(false);
        foreach (var roleId in roleIds.Distinct())
        {
            if (!activeRoles.ContainsKey(roleId)) continue;
            var mysql = databaseOptions.Value.Provider == DatabaseProvider.MySql;
            var parameters = IdentitySqlParameters.Create(("RoleIds", new[] { roleId }), ("MemberLimit", maximumMembersPerRole));
            var statement = mysql ? WorkflowRoleMemberSql.ListActiveHostRoleMembersByRoleIdsMySql
                : WorkflowRoleMemberSql.ListActiveHostRoleMembersByRoleIds;
            if (!currentTenant.IsHost)
            {
                var scope = ResolveTenantScope();
                parameters["TenantId"] = scope.TenantId;
                parameters["TenantScopeKey"] = scope.TenantScopeKey;
                statement = mysql ? WorkflowRoleMemberSql.ListActiveTenantRoleMembersByRoleIdsMySql
                    : WorkflowRoleMemberSql.ListActiveTenantRoleMembersByRoleIds;
            }
            var rows = await queryExecutor.QueryAsync<WorkflowRoleMemberRow>(statement, parameters, cancellationToken).ConfigureAwait(false);
            // 活动空角色保留键，缺键仅表示无效来源；无人策略不能靠猜测角色是否仍存在。
            result[roleId] = rows.Select(row => row.UserId).Distinct().ToArray();
        }
        return result;
    }

    /// <summary>将数据库投影映射为 Workflow Contract 最小角色条目。</summary>
    /// <param name="row">角色列表行。</param>
    /// <returns>角色目录项。</returns>
    private static WorkflowRoleDirectoryEntry Map(WorkflowRoleListRow row) =>
        new(row.Id, row.Code, row.Name);

    /// <summary>从可信上下文解析 Tenant 查询参数。</summary>
    /// <returns>当前 Tenant 作用域。</returns>
    /// <exception cref="TenantContextMissingException">当前请求不处于有效 Tenant 上下文时抛出。</exception>
    private TenantSelectionScope ResolveTenantScope()
    {
        if (!currentTenant.IsHost && currentTenant.IsAvailable && currentTenant.Id is { } tenantId)
        {
            return new TenantSelectionScope(tenantId, $"tenant:{tenantId:N}");
        }

        throw new TenantContextMissingException("identity.tenant_context_required");
    }

    /// <summary>封装 Tenant 角色查询参数。</summary>
    /// <param name="TenantId">当前租户标识。</param>
    /// <param name="TenantScopeKey">规范租户作用域键。</param>
    private readonly record struct TenantSelectionScope(Guid TenantId, string TenantScopeKey);
}
