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
        WHERE Id = @UnitId AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement CountUnits = new(
        "organization.count_units",
        """
        SELECT COUNT(1)
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement CountUserUnits = new(
        "organization.count_user_units",
        """
        SELECT COUNT(1)
        FROM fn_organization_user_unit AS assignment
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@UnitId IS NULL OR assignment.UnitId = @UnitId)
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement ListUserUnitsSqlServer = new(
        "organization.list_user_units.sql_server",
        """
        SELECT assignment.Id, assignment.UserId, userObject.Username, userObject.DisplayName,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_identity_user AS userObject
            ON userObject.Id = assignment.UserId
           AND userObject.ScopeKey = 'host'
           AND userObject.TenantId IS NULL
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@UnitId IS NULL OR assignment.UnitId = @UnitId)
        ORDER BY assignment.IsPrimary DESC, unitObject.DisplayOrder, unitObject.Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement ListUserUnitsMySql = new(
        "organization.list_user_units.mysql",
        """
        SELECT assignment.Id, assignment.UserId, userObject.Username, userObject.DisplayName,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_identity_user AS userObject
            ON userObject.Id = assignment.UserId
           AND userObject.ScopeKey = 'host'
           AND userObject.TenantId IS NULL
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.TenantId = @TenantId
          AND (@UserId IS NULL OR assignment.UserId = @UserId)
          AND (@UnitId IS NULL OR assignment.UnitId = @UnitId)
        ORDER BY assignment.IsPrimary DESC, unitObject.DisplayOrder, unitObject.Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement FindUserUnitById = new(
        "organization.find_user_unit_by_id",
        """
        SELECT assignment.Id, assignment.UserId, userObject.Username, userObject.DisplayName,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_identity_user AS userObject
            ON userObject.Id = assignment.UserId
           AND userObject.ScopeKey = 'host'
           AND userObject.TenantId IS NULL
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
        WHERE assignment.Id = @AssignmentId
          AND assignment.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);
}
