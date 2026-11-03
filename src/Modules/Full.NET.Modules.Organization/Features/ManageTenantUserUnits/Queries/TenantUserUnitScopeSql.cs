using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits.Queries;

/// <summary>先在组织模块内解析可见机构，避免将动态权限片段拼入受控跨模块查询。</summary>
internal static class TenantUserUnitScopeSql
{
    public static readonly SqlStatement VisibleUnits = new(
        "organization.user_unit_list.visible_units",
        """
        SELECT Id FROM fn_organization_unit WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    // SQL Server 的递归 CTE 必须位于语句顶层；仅沿启用的主机构及其启用后代展开。
    public static readonly SqlStatement SubtreeSqlServer = new(
        "organization.user_unit_list.scope_subtree.sql_server",
        """
        WITH unit_tree AS (
            SELECT unitObject.Id
            FROM fn_organization_unit AS unitObject
            INNER JOIN fn_organization_user_unit AS ownAssignment
                ON ownAssignment.UnitId = unitObject.Id
               AND ownAssignment.TenantId = unitObject.TenantId
            WHERE unitObject.TenantId = @TenantId
              AND unitObject.IsActive = 1
              AND ownAssignment.UserId = @DataScopeUserId
              AND ownAssignment.IsPrimary = 1 AND ownAssignment.IsActive = 1
            UNION ALL
            SELECT childObject.Id
            FROM fn_organization_unit AS childObject
            INNER JOIN unit_tree ON childObject.ParentId = unit_tree.Id
            WHERE childObject.TenantId = @TenantId AND childObject.IsActive = 1
        )
        SELECT DISTINCT Id FROM unit_tree
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    // MySQL 以 WITH RECURSIVE 显式启用递归，集合语义与 SQL Server 相同。
    public static readonly SqlStatement SubtreeMySql = new(
        "organization.user_unit_list.scope_subtree.mysql",
        """
        WITH RECURSIVE unit_tree AS (
            SELECT unitObject.Id
            FROM fn_organization_unit AS unitObject
            INNER JOIN fn_organization_user_unit AS ownAssignment
                ON ownAssignment.UnitId = unitObject.Id
               AND ownAssignment.TenantId = unitObject.TenantId
            WHERE unitObject.TenantId = @TenantId
              AND unitObject.IsActive = 1
              AND ownAssignment.UserId = @DataScopeUserId
              AND ownAssignment.IsPrimary = 1 AND ownAssignment.IsActive = 1
            UNION ALL
            SELECT childObject.Id
            FROM fn_organization_unit AS childObject
            INNER JOIN unit_tree ON childObject.ParentId = unit_tree.Id
            WHERE childObject.TenantId = @TenantId AND childObject.IsActive = 1
        )
        SELECT DISTINCT Id FROM unit_tree
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
