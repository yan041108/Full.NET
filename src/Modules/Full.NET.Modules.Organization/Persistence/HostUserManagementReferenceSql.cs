using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>Host 用户管理页按租户读取机构参考数据时使用的 Global 租户探测 SQL。</summary>
internal static class HostUserManagementReferenceSql
{
    /// <summary>Host 用户管理页读取租户有效机构，不受操作者机构数据范围裁剪。</summary>
    public static readonly SqlStatement ListUnits = new(
        "organization.host_user_management.list_units",
        """
        SELECT Id, ParentId, Code, Name, DisplayOrder, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
          AND IsActive = 1
        ORDER BY DisplayOrder, Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>Host 用户管理页读取租户有效用户-机构隶属，不受操作者机构数据范围裁剪。</summary>
    public static readonly SqlStatement ListUserUnits = new(
        "organization.host_user_management.list_user_units",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
           AND unitObject.IsActive = 1
        WHERE assignment.TenantId = @TenantId
          AND assignment.IsActive = 1
        ORDER BY assignment.IsPrimary DESC, unitObject.DisplayOrder, unitObject.Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement CountUnits = new(
        "organization.host_user_management.count_units",
        """
        SELECT COUNT(1) FROM fn_organization_unit
        WHERE TenantId = @TenantId AND IsActive = 1 AND (@UnitSearch IS NULL OR Name LIKE @UnitSearch ESCAPE '!' OR Code LIKE @UnitSearch ESCAPE '!')
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement CountPositions = new(
        "organization.host_user_management.count_positions",
        """
        SELECT COUNT(1) FROM fn_organization_position
        WHERE TenantId = @TenantId AND IsActive = 1 AND (@PositionSearch IS NULL OR Name LIKE @PositionSearch ESCAPE '!' OR Code LIKE @PositionSearch ESCAPE '!')
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListUnitsPageSqlServer = new(
        "organization.host_user_management.units_page.sqlserver",
        ListUnits.Text.Replace("AND IsActive = 1", "AND IsActive = 1 AND (@UnitSearch IS NULL OR Name LIKE @UnitSearch ESCAPE '!' OR Code LIKE @UnitSearch ESCAPE '!')", StringComparison.Ordinal) + " OFFSET @UnitOffset ROWS FETCH NEXT 100 ROWS ONLY",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListUnitsPageMySql = new(
        "organization.host_user_management.units_page.mysql",
        ListUnits.Text.Replace("AND IsActive = 1", "AND IsActive = 1 AND (@UnitSearch IS NULL OR Name LIKE @UnitSearch ESCAPE '!' OR Code LIKE @UnitSearch ESCAPE '!')", StringComparison.Ordinal) + " LIMIT 100 OFFSET @UnitOffset",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListUserUnitsPageSqlServer = new(
        "organization.host_user_management.userunits_page.sqlserver",
        ListUserUnits.Text.Replace("AND assignment.IsActive = 1", "AND assignment.IsActive = 1 AND assignment.UserId IN @UserIds", StringComparison.Ordinal) + " OFFSET 0 ROWS FETCH NEXT 10001 ROWS ONLY",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListUserUnitsPageMySql = new(
        "organization.host_user_management.userunits_page.mysql",
        ListUserUnits.Text.Replace("AND assignment.IsActive = 1", "AND assignment.IsActive = 1 AND assignment.UserId IN @UserIds", StringComparison.Ordinal) + " LIMIT 10001",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListPositionsPageSqlServer = new(
        "organization.host_user_management.positions_page.sqlserver",
        PositionSql.ListSqlServer.Text.Replace("WHERE positionObject.TenantId = @TenantId", "WHERE positionObject.TenantId = @TenantId AND positionObject.IsActive = 1 AND (@PositionSearch IS NULL OR positionObject.Name LIKE @PositionSearch ESCAPE '!' OR positionObject.Code LIKE @PositionSearch ESCAPE '!')", StringComparison.Ordinal).Replace("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", "", StringComparison.Ordinal) + " OFFSET @PositionOffset ROWS FETCH NEXT 100 ROWS ONLY",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListPositionsPageMySql = new(
        "organization.host_user_management.positions_page.mysql",
        PositionSql.ListSqlServer.Text.Replace("WHERE positionObject.TenantId = @TenantId", "WHERE positionObject.TenantId = @TenantId AND positionObject.IsActive = 1 AND (@PositionSearch IS NULL OR positionObject.Name LIKE @PositionSearch ESCAPE '!' OR positionObject.Code LIKE @PositionSearch ESCAPE '!')", StringComparison.Ordinal).Replace("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", "", StringComparison.Ordinal) + " LIMIT 100 OFFSET @PositionOffset",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListUserPositionsPageSqlServer = new(
        "organization.host_user_management.userpositions_page.sqlserver",
        OrganizationSql.ListUserPositionsSqlServer.Text.Replace("AND (@UserId IS NULL OR assignment.UserId = @UserId)", "AND assignment.IsActive = 1 AND assignment.UserId IN @UserIds", StringComparison.Ordinal).Replace("AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)", "", StringComparison.Ordinal).Replace("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", "", StringComparison.Ordinal) + " OFFSET 0 ROWS FETCH NEXT 10001 ROWS ONLY",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListUserPositionsPageMySql = new(
        "organization.host_user_management.userpositions_page.mysql",
        OrganizationSql.ListUserPositionsSqlServer.Text.Replace("AND (@UserId IS NULL OR assignment.UserId = @UserId)", "AND assignment.IsActive = 1 AND assignment.UserId IN @UserIds", StringComparison.Ordinal).Replace("AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)", "", StringComparison.Ordinal).Replace("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", "", StringComparison.Ordinal) + " LIMIT 10001",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
