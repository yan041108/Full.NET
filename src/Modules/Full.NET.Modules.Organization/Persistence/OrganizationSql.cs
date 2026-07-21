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
}
