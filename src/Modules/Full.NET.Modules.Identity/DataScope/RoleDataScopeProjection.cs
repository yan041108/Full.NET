namespace Full.NET.Modules.Identity.DataScope;

using Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 角色数据范围在机构单元查询上的参数化 SQL 投影。
/// </summary>
internal static class RoleDataScopeProjection
{
    /// <summary>
    /// 为机构单元 Id 列构建附加 WHERE 片段；返回 null 表示不追加限制（全部）。
    /// </summary>
    public static RoleDataScopeSqlFragment? BuildOrganizationUnitFilter(
        string dataScopeKind,
        string unitIdColumn,
        Guid? currentUserId = null)
    {
        return dataScopeKind switch
        {
            RoleDataScopeKinds.All => null,
            RoleDataScopeKinds.Self => new RoleDataScopeSqlFragment(
                $"""
                EXISTS (
                    SELECT 1
                    FROM fn_organization_user_unit AS assignment
                    WHERE assignment.TenantId = @TenantId
                      AND assignment.UserId = @DataScopeUserId
                      AND assignment.UnitId = {unitIdColumn}
                      AND assignment.IsActive = 1
                )
                """,
                new { DataScopeUserId = currentUserId }),
            RoleDataScopeKinds.Organization => new RoleDataScopeSqlFragment(
                $"""
                {unitIdColumn} IN (
                    SELECT assignment.UnitId
                    FROM fn_organization_user_unit AS assignment
                    WHERE assignment.TenantId = @TenantId
                      AND assignment.UserId = @DataScopeUserId
                      AND assignment.IsPrimary = 1
                      AND assignment.IsActive = 1
                )
                """,
                new { DataScopeUserId = currentUserId }),
            RoleDataScopeKinds.OrganizationSubtree => new RoleDataScopeSqlFragment(
                BuildSubtreeSql(unitIdColumn),
                new { DataScopeUserId = currentUserId }),
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

    private static string BuildSubtreeSql(string unitIdColumn) =>
        $"""
        {unitIdColumn} IN (
            WITH primary_unit AS (
                SELECT assignment.UnitId
                FROM fn_organization_user_unit AS assignment
                WHERE assignment.TenantId = @TenantId
                  AND assignment.UserId = @DataScopeUserId
                  AND assignment.IsPrimary = 1
                  AND assignment.IsActive = 1
            ),
            unit_tree AS (
                SELECT unitObject.Id
                FROM fn_organization_unit AS unitObject
                INNER JOIN primary_unit
                    ON primary_unit.UnitId = unitObject.Id
                WHERE unitObject.TenantId = @TenantId
                  AND unitObject.IsActive = 1
                UNION ALL
                SELECT childObject.Id
                FROM fn_organization_unit AS childObject
                INNER JOIN unit_tree
                    ON childObject.ParentId = unit_tree.Id
                WHERE childObject.TenantId = @TenantId
                  AND childObject.IsActive = 1
            )
            SELECT unit_tree.Id FROM unit_tree
        )
        """;
}

internal sealed record RoleDataScopeSqlFragment(
    string Sql,
    object? Parameters);
