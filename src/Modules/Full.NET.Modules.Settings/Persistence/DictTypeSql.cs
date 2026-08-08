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

    /// <summary>全量 Host 字典类型列表（不分页），供下拉与全量消费场景使用。</summary>
    public static readonly SqlStatement ListAllHostDictTypes = new(
        "settings.dict_type.list_all",
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
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 硬删除 Host 字典类型；前置校验 IsActive=0 与无活跃字典项由 Service 保证，
    /// WHERE 同时校验 IsActive=0 与 Version 做防御性兜底与并发控制。
    /// </summary>
    public static readonly SqlStatement DeleteDictType = new(
        "settings.dict_type.delete",
        """
        DELETE FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId IS NULL
          AND IsActive = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 级联删除字典类型下的全部字典项（含已禁用），在删除类型本身前于同一事务内执行。
    /// </summary>
    public static readonly SqlStatement DeleteItemsByType = new(
        "settings.dict_item.delete_by_type",
        """
        DELETE FROM fn_settings_dict_item
        WHERE DictTypeId = @DictTypeId
        """,
        SqlDataScope.HostOnly);
}
