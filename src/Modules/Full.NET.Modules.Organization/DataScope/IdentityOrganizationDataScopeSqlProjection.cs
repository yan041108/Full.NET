using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Organization.DataScope;

/// <summary>
/// 将 Identity 请求的机构数据范围投影为 Organization 自有表上的参数化 SQL。
/// </summary>
/// <remarks>
/// 列名由 Organization 内部查询传入，不属于请求数据；用户与租户值始终通过参数绑定，
/// 从而在保留原有 SQL 组合方式的同时收回机构表结构所有权。
/// </remarks>
internal sealed class IdentityOrganizationDataScopeSqlProjection
    : IIdentityOrganizationDataScopeSqlProjection
{
    public DataScopeSqlFilter BuildOrganizationUnitFilter(
        string dataScopeKind,
        string unitIdColumn,
        Guid currentUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataScopeKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitIdColumn);

        var sql = dataScopeKind switch
        {
            RoleDataScopeKinds.Self => BuildSelfSql(unitIdColumn),
            RoleDataScopeKinds.Organization => BuildOrganizationSql(unitIdColumn),
            RoleDataScopeKinds.OrganizationSubtree => BuildSubtreeSql(unitIdColumn),
            _ => throw new ArgumentException(
                "The data scope kind is not owned by the Organization projection.",
                nameof(dataScopeKind)),
        };

        return new DataScopeSqlFilter(
            sql,
            new { DataScopeUserId = currentUserId });
    }

    private static string BuildSelfSql(string unitIdColumn) =>
        $"""
        {unitIdColumn} IN (
            SELECT assignment.UnitId
            FROM fn_organization_user_unit AS assignment
            WHERE assignment.TenantId = @TenantId
              AND assignment.UserId = @DataScopeUserId
              AND assignment.IsActive = 1
        )
        """;

    private static string BuildOrganizationSql(string unitIdColumn) =>
        $"""
        {unitIdColumn} IN (
            SELECT assignment.UnitId
            FROM fn_organization_user_unit AS assignment
            WHERE assignment.TenantId = @TenantId
              AND assignment.UserId = @DataScopeUserId
              AND assignment.IsPrimary = 1
              AND assignment.IsActive = 1
        )
        """;

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
