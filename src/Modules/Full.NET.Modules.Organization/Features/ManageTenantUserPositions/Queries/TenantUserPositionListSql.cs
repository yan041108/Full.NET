using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserPositions.Queries;

/// <summary>后台用户职位隶属查询；Host 用户条件、租户和计数筛选必须与列表一致。</summary>
/// <remarks>展示当前用户名；停用用户仍可展示，失效用户关联在分页前排除。唯一关系 Id 保证同排序值下分页稳定。</remarks>
internal static class TenantUserPositionListSql
{
    public static readonly SqlStatement CountSqlServer = new(
        "organization.count_user_positions.admin.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS objectRow
            ON objectRow.Id = assignment.PositionId
           AND objectRow.TenantId = assignment.TenantId
        INNER JOIN fn_identity_user AS hostUser
            ON hostUser.Id = assignment.UserId
           AND hostUser.ScopeKey = 'host'
           AND hostUser.TenantId IS NULL
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
          AND (@IsActive IS NULL OR assignment.IsActive = @IsActive)
          AND (@UserSearch IS NULL OR LOWER(hostUser.Username) LIKE LOWER(@UserSearch) ESCAPE '!' OR LOWER(hostUser.DisplayName) LIKE LOWER(@UserSearch) ESCAPE '!')
          AND (@PositionSearch IS NULL OR LOWER(objectRow.Code) LIKE LOWER(@PositionSearch) ESCAPE '!' OR LOWER(objectRow.Name) LIKE LOWER(@PositionSearch) ESCAPE '!')
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServer = new(
        "organization.list_user_positions.admin.sql_server",
        """
        SELECT assignment.Id, assignment.UserId, hostUser.Username, hostUser.DisplayName,
               assignment.PositionId, objectRow.Code AS PositionCode, objectRow.Name AS PositionName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS objectRow
            ON objectRow.Id = assignment.PositionId
           AND objectRow.TenantId = assignment.TenantId
        INNER JOIN fn_identity_user AS hostUser
            ON hostUser.Id = assignment.UserId
           AND hostUser.ScopeKey = 'host'
           AND hostUser.TenantId IS NULL
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
          AND (@IsActive IS NULL OR assignment.IsActive = @IsActive)
          AND (@UserSearch IS NULL OR LOWER(hostUser.Username) LIKE LOWER(@UserSearch) ESCAPE '!' OR LOWER(hostUser.DisplayName) LIKE LOWER(@UserSearch) ESCAPE '!')
          AND (@PositionSearch IS NULL OR LOWER(objectRow.Code) LIKE LOWER(@PositionSearch) ESCAPE '!' OR LOWER(objectRow.Name) LIKE LOWER(@PositionSearch) ESCAPE '!')
        ORDER BY assignment.IsPrimary DESC, objectRow.DisplayOrder, objectRow.Code, assignment.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountMySql = new(
        "organization.count_user_positions.admin.mysql",
        """
        SELECT COUNT(1)
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS objectRow
            ON objectRow.Id = assignment.PositionId
           AND objectRow.TenantId = assignment.TenantId
        INNER JOIN fn_identity_user AS hostUser
            ON hostUser.Id = assignment.UserId
           AND hostUser.ScopeKey = 'host'
           AND hostUser.TenantId IS NULL
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
          AND (@IsActive IS NULL OR assignment.IsActive = @IsActive)
          AND (@UserSearch IS NULL OR LOWER(hostUser.Username) LIKE LOWER(@UserSearch) ESCAPE '!' OR LOWER(hostUser.DisplayName) LIKE LOWER(@UserSearch) ESCAPE '!')
          AND (@PositionSearch IS NULL OR LOWER(objectRow.Code) LIKE LOWER(@PositionSearch) ESCAPE '!' OR LOWER(objectRow.Name) LIKE LOWER(@PositionSearch) ESCAPE '!')
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySql = new(
        "organization.list_user_positions.admin.mysql",
        """
        SELECT assignment.Id, assignment.UserId, hostUser.Username, hostUser.DisplayName,
               assignment.PositionId, objectRow.Code AS PositionCode, objectRow.Name AS PositionName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS objectRow
            ON objectRow.Id = assignment.PositionId
           AND objectRow.TenantId = assignment.TenantId
        INNER JOIN fn_identity_user AS hostUser
            ON hostUser.Id = assignment.UserId
           AND hostUser.ScopeKey = 'host'
           AND hostUser.TenantId IS NULL
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
          AND (@IsActive IS NULL OR assignment.IsActive = @IsActive)
          AND (@UserSearch IS NULL OR LOWER(hostUser.Username) LIKE LOWER(@UserSearch) ESCAPE '!' OR LOWER(hostUser.DisplayName) LIKE LOWER(@UserSearch) ESCAPE '!')
          AND (@PositionSearch IS NULL OR LOWER(objectRow.Code) LIKE LOWER(@PositionSearch) ESCAPE '!' OR LOWER(objectRow.Name) LIKE LOWER(@PositionSearch) ESCAPE '!')
        ORDER BY assignment.IsPrimary DESC, objectRow.DisplayOrder, objectRow.Code, assignment.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
