using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

internal static class PositionSql
{
    public static readonly SqlStatement FindById = new(
        "organization.find_position_by_id",
        """
        SELECT Id, TenantId, Code, Name, DisplayOrder,
               IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_position
        WHERE Id = @PositionId AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement FindByTenantAndCode = new(
        "organization.find_position_by_tenant_and_code",
        """
        SELECT Id, TenantId, Code, Name, DisplayOrder,
               IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_position
        WHERE TenantId = @TenantId AND Code = @Code
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement Count = new(
        "organization.count_positions",
        """
        SELECT COUNT(1)
        FROM fn_organization_position
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement ListSqlServer = new(
        "organization.list_positions.sql_server",
        """
        SELECT Id, Code, Name, DisplayOrder, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_position
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement ListMySql = new(
        "organization.list_positions.mysql",
        """
        SELECT Id, Code, Name, DisplayOrder, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_position
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement Insert = new(
        "organization.insert_position",
        """
        INSERT INTO fn_organization_position
            (Id, TenantId, Code, Name, DisplayOrder,
             IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Code, @Name, @DisplayOrder,
             @IsActive, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);

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
        SqlDataScope.TenantRequired);
}
