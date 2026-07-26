namespace Full.NET.Modules.Identity.DataScope;

using Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 角色数据范围在机构单元查询上的参数化 SQL 投影。
/// </summary>
internal sealed class RoleDataScopeProjection
{
    private readonly IIdentityOrganizationDataScopeSqlProjection? organizationProjection;

    /// <summary>
    /// 允许 Identity 在未装配 Organization 的精简宿主中独立启动；机构范围只有在真实
    /// 消费时才要求存在唯一适配器，避免把可选模块变成 Identity 的启动期反向依赖。
    /// </summary>
    public RoleDataScopeProjection(
        IEnumerable<IIdentityOrganizationDataScopeSqlProjection> organizationProjections)
    {
        ArgumentNullException.ThrowIfNull(organizationProjections);
        organizationProjection = organizationProjections.SingleOrDefault();
    }

    /// <summary>
    /// 为机构单元 Id 列构建附加 WHERE 片段；返回 null 表示不追加限制（全部）。
    /// </summary>
    public RoleDataScopeSqlFragment? BuildOrganizationUnitFilter(
        string dataScopeKind,
        string unitIdColumn,
        Guid? currentUserId = null)
    {
        return dataScopeKind switch
        {
            RoleDataScopeKinds.All => null,
            RoleDataScopeKinds.Self
                or RoleDataScopeKinds.Organization
                or RoleDataScopeKinds.OrganizationSubtree =>
                BuildOrganizationFilter(dataScopeKind, unitIdColumn, currentUserId),
            RoleDataScopeKinds.Custom => new RoleDataScopeSqlFragment(
                $"""
                {unitIdColumn} IN (
                    SELECT scopeUnit.UnitId
                    FROM fn_identity_role_data_scope_unit AS scopeUnit
                    WHERE scopeUnit.RoleId = @DataScopeRoleId
                )
                """,
                null),
            _ => throw new ArgumentException(
                "The data scope kind is not supported.",
                nameof(dataScopeKind)),
        };
    }

    /// <summary>
    /// 多角色数据范围并集；返回 null 表示不限制，<c>1 = 0</c> 表示无可见数据。
    /// </summary>
    public RoleDataScopeSqlFragment? BuildUnionOrganizationUnitFilter(
        IReadOnlyList<RoleDataScopeEntry> roleScopes,
        string unitIdColumn,
        Guid currentUserId)
    {
        ArgumentNullException.ThrowIfNull(roleScopes);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitIdColumn);

        if (roleScopes.Count == 0)
        {
            return DenyAll();
        }

        var parts = new List<string>();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["DataScopeUserId"] = currentUserId,
        };
        var customRoleIndex = 0;

        foreach (var roleScope in roleScopes)
        {
            if (string.Equals(
                    roleScope.DataScopeKind,
                    RoleDataScopeKinds.All,
                    StringComparison.Ordinal))
            {
                return null;
            }

            var fragment = BuildOrganizationUnitFilter(
                roleScope.DataScopeKind,
                unitIdColumn,
                currentUserId);
            if (fragment is null)
            {
                return null;
            }

            var sql = fragment.Sql;
            if (string.Equals(
                    roleScope.DataScopeKind,
                    RoleDataScopeKinds.Custom,
                    StringComparison.Ordinal))
            {
                var parameterName = $"DataScopeRoleId_{customRoleIndex}";
                sql = sql.Replace("@DataScopeRoleId", $"@{parameterName}", StringComparison.Ordinal);
                parameters[parameterName] = roleScope.RoleId;
                customRoleIndex++;
            }

            parts.Add($"({sql})");
        }

        if (parts.Count == 0)
        {
            return DenyAll();
        }

        return new RoleDataScopeSqlFragment(string.Join(" OR ", parts), parameters);
    }

    private static RoleDataScopeSqlFragment DenyAll() =>
        new("1 = 0", null);

    private RoleDataScopeSqlFragment BuildOrganizationFilter(
        string dataScopeKind,
        string unitIdColumn,
        Guid? currentUserId)
    {
        var projection = organizationProjection
            ?? throw new InvalidOperationException(
                "The Organization data-scope projection is not registered.");
        var filter = projection.BuildOrganizationUnitFilter(
            dataScopeKind,
            unitIdColumn,
            currentUserId ?? Guid.Empty);
        return new RoleDataScopeSqlFragment(filter.Sql, filter.Parameters);
    }
}

internal sealed record RoleDataScopeSqlFragment(
    string Sql,
    object? Parameters);
