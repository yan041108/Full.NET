using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

internal static class OrganizationSql
{
    public static readonly SqlStatement FindUnitById = new(
        "organization.find_unit_by_id",
        """
        SELECT Id, TenantId, ParentId, Code, Name, DisplayOrder,
               IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE TenantId = @TenantId AND Id = @UnitId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindActiveUnitByTenantAndId = new(
        "organization.find_active_unit_by_tenant_and_id",
        """
        SELECT Id, TenantId, ParentId, Code, Name, DisplayOrder,
               IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE Id = @UnitId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindUnitByTenantAndCode = new(
        "organization.find_unit_by_tenant_and_code",
        """
        SELECT Id, TenantId, ParentId, Code, Name, DisplayOrder,
               IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE TenantId = @TenantId AND Code = @Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountUnits = new(
        "organization.count_units",
        """
        SELECT COUNT(1)
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUnitsSqlServer = new(
        "organization.list_units.sql_server",
        """
        SELECT Id, ParentId, Code, Name, DisplayOrder, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUnitsMySql = new(
        "organization.list_units.mysql",
        """
        SELECT Id, ParentId, Code, Name, DisplayOrder, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertUnit = new(
        "organization.insert_unit",
        """
        INSERT INTO fn_organization_unit
            (Id, TenantId, ParentId, Code, Name, DisplayOrder,
             IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ParentId, @Code, @Name, @DisplayOrder,
             @IsActive, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateUnit = new(
        "organization.update_unit",
        """
        UPDATE fn_organization_unit
        SET ParentId = @ParentId,
            Name = @Name,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UnitId
          AND TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableUnit = new(
        "organization.disable_unit",
        """
        UPDATE fn_organization_unit
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UnitId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountUserUnits = new(
        "organization.count_user_units",
        """
        SELECT COUNT(1)
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@UnitId IS NULL OR assignment.UnitId = @UnitId)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUserUnitsSqlServer = new(
        "organization.list_user_units.sql_server",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@UnitId IS NULL OR assignment.UnitId = @UnitId)
        ORDER BY assignment.IsPrimary DESC, unitObject.DisplayOrder, unitObject.Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUserUnitsMySql = new(
        "organization.list_user_units.mysql",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@UnitId IS NULL OR assignment.UnitId = @UnitId)
        ORDER BY assignment.IsPrimary DESC, unitObject.DisplayOrder, unitObject.Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindUserUnitById = new(
        "organization.find_user_unit_by_id",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.Id = @AssignmentId
          AND assignment.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindUserUnitByTenantUserAndUnit = new(
        "organization.find_user_unit_by_tenant_user_and_unit",
        """
        SELECT Id, TenantId, UserId, UnitId, IsPrimary, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_user_unit
        WHERE TenantId = @TenantId
          AND UserId = @UserId
          AND UnitId = @UnitId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertUserUnit = new(
        "organization.insert_user_unit",
        """
        INSERT INTO fn_organization_user_unit
            (Id, TenantId, UserId, UnitId, IsPrimary, IsActive,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @UserId, @UnitId, @IsPrimary, @IsActive,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClearPrimaryUserUnits = new(
        "organization.clear_primary_user_units",
        """
        UPDATE fn_organization_user_unit
        SET IsPrimary = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId
          AND UserId = @UserId
          AND IsPrimary = 1
          AND IsActive = 1
          AND Id <> @AssignmentId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateUserUnitPrimary = new(
        "organization.update_user_unit_primary",
        """
        UPDATE fn_organization_user_unit
        SET IsPrimary = @IsPrimary,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @AssignmentId
          AND TenantId = @TenantId
          AND Version = @Version
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableUserUnit = new(
        "organization.disable_user_unit",
        """
        UPDATE fn_organization_user_unit
        SET IsActive = 0,
            IsPrimary = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @AssignmentId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountUserPositions = new(
        "organization.count_user_positions",
        """
        SELECT COUNT(1)
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS positionObject
            ON positionObject.Id = assignment.PositionId
           AND positionObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUserPositionsSqlServer = new(
        "organization.list_user_positions.sql_server",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.PositionId, positionObject.Code AS PositionCode, positionObject.Name AS PositionName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS positionObject
            ON positionObject.Id = assignment.PositionId
           AND positionObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
        ORDER BY assignment.IsPrimary DESC, positionObject.DisplayOrder, positionObject.Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListUserPositionsMySql = new(
        "organization.list_user_positions.mysql",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.PositionId, positionObject.Code AS PositionCode, positionObject.Name AS PositionName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS positionObject
            ON positionObject.Id = assignment.PositionId
           AND positionObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@PositionId IS NULL OR assignment.PositionId = @PositionId)
        ORDER BY assignment.IsPrimary DESC, positionObject.DisplayOrder, positionObject.Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindUserPositionById = new(
        "organization.find_user_position_by_id",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.PositionId, positionObject.Code AS PositionCode, positionObject.Name AS PositionName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_position AS assignment
        INNER JOIN fn_organization_position AS positionObject
            ON positionObject.Id = assignment.PositionId
           AND positionObject.TenantId = assignment.TenantId
        WHERE assignment.Id = @AssignmentId
          AND assignment.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindUserPositionByTenantUserAndPosition = new(
        "organization.find_user_position_by_tenant_user_and_position",
        """
        SELECT Id, TenantId, UserId, PositionId, IsPrimary, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_user_position
        WHERE TenantId = @TenantId
          AND UserId = @UserId
          AND PositionId = @PositionId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertUserPosition = new(
        "organization.insert_user_position",
        """
        INSERT INTO fn_organization_user_position
            (Id, TenantId, UserId, PositionId, IsPrimary, IsActive,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @UserId, @PositionId, @IsPrimary, @IsActive,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClearPrimaryUserPositions = new(
        "organization.clear_primary_user_positions",
        """
        UPDATE fn_organization_user_position
        SET IsPrimary = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId
          AND UserId = @UserId
          AND IsPrimary = 1
          AND IsActive = 1
          AND Id <> @AssignmentId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateUserPositionPrimary = new(
        "organization.update_user_position_primary",
        """
        UPDATE fn_organization_user_position
        SET IsPrimary = @IsPrimary,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @AssignmentId
          AND TenantId = @TenantId
          AND Version = @Version
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableUserPosition = new(
        "organization.disable_user_position",
        """
        UPDATE fn_organization_user_position
        SET IsActive = 0,
            IsPrimary = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @AssignmentId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
