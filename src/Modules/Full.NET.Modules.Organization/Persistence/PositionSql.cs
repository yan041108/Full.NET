using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>
/// 租户职位目录的参数化 SQL 集合，按 <c>TenantRequired</c> 作用域绑定当前租户。
/// </summary>
/// <remarks>
/// 职位查询通过 <c>LEFT JOIN</c> 关联本模块拥有的 <c>fn_organization_unit</c> 与 <c>fn_organization_position_level</c>，
/// 仅用于补全展示字段，不构成跨模块读；职级缺失时仍可返回职位主体。
/// </remarks>
internal static class PositionSql
{
    public static readonly SqlStatement FindById = new(
        "organization.find_position_by_id",
        """
        SELECT positionObject.Id, positionObject.TenantId,
               positionObject.Code, positionObject.Name,
               positionObject.UnitId, unitObject.Code AS UnitCode,
               unitObject.Name AS UnitName, positionObject.PositionLevelId,
               positionLevelObject.Code AS PositionLevelCode,
               positionLevelObject.Name AS PositionLevelName,
               positionObject.DisplayOrder,
               positionObject.IsActive, positionObject.CreatedAtUtc,
               positionObject.UpdatedAtUtc, positionObject.Version
        FROM fn_organization_position AS positionObject
        LEFT JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = positionObject.UnitId
           AND unitObject.TenantId = positionObject.TenantId
        LEFT JOIN fn_organization_position_level AS positionLevelObject
            ON positionLevelObject.Id = positionObject.PositionLevelId
           AND positionLevelObject.TenantId = positionObject.TenantId
        WHERE positionObject.Id = @PositionId
          AND positionObject.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByTenantAndCode = new(
        "organization.find_position_by_tenant_and_code",
        """
        SELECT positionObject.Id, positionObject.TenantId,
               positionObject.Code, positionObject.Name,
               positionObject.UnitId, unitObject.Code AS UnitCode,
               unitObject.Name AS UnitName, positionObject.PositionLevelId,
               positionLevelObject.Code AS PositionLevelCode,
               positionLevelObject.Name AS PositionLevelName,
               positionObject.DisplayOrder,
               positionObject.IsActive, positionObject.CreatedAtUtc,
               positionObject.UpdatedAtUtc, positionObject.Version
        FROM fn_organization_position AS positionObject
        LEFT JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = positionObject.UnitId
           AND unitObject.TenantId = positionObject.TenantId
        LEFT JOIN fn_organization_position_level AS positionLevelObject
            ON positionLevelObject.Id = positionObject.PositionLevelId
           AND positionLevelObject.TenantId = positionObject.TenantId
        WHERE positionObject.TenantId = @TenantId
          AND positionObject.Code = @Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Count = new(
        "organization.count_positions",
        """
        SELECT COUNT(1)
        FROM fn_organization_position
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServer = new(
        "organization.list_positions.sql_server",
        """
        SELECT positionObject.Id, positionObject.Code, positionObject.Name,
               positionObject.UnitId, unitObject.Code AS UnitCode,
               unitObject.Name AS UnitName, positionObject.PositionLevelId,
               positionLevelObject.Code AS PositionLevelCode,
               positionLevelObject.Name AS PositionLevelName,
               positionObject.DisplayOrder,
               positionObject.IsActive, positionObject.CreatedAtUtc,
               positionObject.UpdatedAtUtc, positionObject.Version
        FROM fn_organization_position AS positionObject
        LEFT JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = positionObject.UnitId
           AND unitObject.TenantId = positionObject.TenantId
        LEFT JOIN fn_organization_position_level AS positionLevelObject
            ON positionLevelObject.Id = positionObject.PositionLevelId
           AND positionLevelObject.TenantId = positionObject.TenantId
        WHERE positionObject.TenantId = @TenantId
        ORDER BY positionObject.DisplayOrder, positionObject.Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySql = new(
        "organization.list_positions.mysql",
        """
        SELECT positionObject.Id, positionObject.Code, positionObject.Name,
               positionObject.UnitId, unitObject.Code AS UnitCode,
               unitObject.Name AS UnitName, positionObject.PositionLevelId,
               positionLevelObject.Code AS PositionLevelCode,
               positionLevelObject.Name AS PositionLevelName,
               positionObject.DisplayOrder,
               positionObject.IsActive, positionObject.CreatedAtUtc,
               positionObject.UpdatedAtUtc, positionObject.Version
        FROM fn_organization_position AS positionObject
        LEFT JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = positionObject.UnitId
           AND unitObject.TenantId = positionObject.TenantId
        LEFT JOIN fn_organization_position_level AS positionLevelObject
            ON positionLevelObject.Id = positionObject.PositionLevelId
           AND positionLevelObject.TenantId = positionObject.TenantId
        WHERE positionObject.TenantId = @TenantId
        ORDER BY positionObject.DisplayOrder, positionObject.Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Insert = new(
        "organization.insert_position",
        """
        INSERT INTO fn_organization_position
            (Id, TenantId, Code, Name, DisplayOrder,
             IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Code, @Name, @DisplayOrder,
             @IsActive, @CreatedAtUtc, NULL, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Update = new(
        "organization.update_position",
        """
        UPDATE fn_organization_position
        SET Name = @Name,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PositionId
          AND TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement AssignUnit = new(
        "organization.assign_position_unit",
        """
        UPDATE fn_organization_position
        SET UnitId = @UnitId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PositionId
          AND TenantId = @TenantId
          AND Version = @Version
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement AssignPositionLevel = new(
        "organization.assign_position_level",
        """
        UPDATE fn_organization_position
        SET PositionLevelId = @PositionLevelId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PositionId
          AND TenantId = @TenantId
          AND Version = @Version
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Disable = new(
        "organization.disable_position",
        """
        UPDATE fn_organization_position
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PositionId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
