using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

internal static class DictTypeSql
{
    public static readonly SqlStatement CountHostDictTypes = new(
        "settings.count_host_dict_types",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_type
        WHERE TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostDictTypesSqlServer = new(
        "settings.list_host_dict_types.sql_server",
        """
        SELECT Id,
               Code,
               Name,
               Description,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_type
        WHERE TenantId IS NULL
        ORDER BY DisplayOrder, Name, Code, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostDictTypesMySql = new(
        "settings.list_host_dict_types.mysql",
        """
        SELECT Id,
               Code,
               Name,
               Description,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_type
        WHERE TenantId IS NULL
        ORDER BY DisplayOrder, Name, Code, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "settings.dict_type.find_by_id",
        """
        SELECT Id,
               Code,
               Name,
               Description,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindIdentityById = new(
        "settings.dict_type.find_identity_by_id",
        """
        SELECT Id, Code, Name, Description, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByCode = new(
        "settings.dict_type.find_by_code",
        """
        SELECT Id, Code, Name, Description, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_type
        WHERE Code = @Code
          AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "settings.dict_type.insert",
        """
        INSERT INTO fn_settings_dict_type
            (Id, TenantId, Code, Name, Description, DisplayOrder, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, NULL, @Code, @Name, @Description, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostDictType = new(
        "settings.dict_type.update",
        """
        UPDATE fn_settings_dict_type
        SET Name = @Name,
            Description = @Description,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictTypeId
          AND TenantId IS NULL
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostDictType = new(
        "settings.dict_type.disable",
        """
        UPDATE fn_settings_dict_type
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictTypeId
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveItems = new(
        "settings.dict_type.count_active_items",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_item
        WHERE DictTypeId = @DictTypeId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);
}
