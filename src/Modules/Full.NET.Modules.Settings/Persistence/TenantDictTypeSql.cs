using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// 租户数据字典类型 SQL 语句集合。提供租户下字典类型的分页列表、全量查询、按 Id/Code 查找、
/// 启用字典项计数、插入、更新、禁用、级联删除字典项后硬删除类型（仅禁用且无活跃项）等语句。
/// 所有语句绑定当前租户上下文（TenantRequired），确保租户间数据隔离。
/// </summary>
internal static class TenantDictTypeSql
{
    public static readonly SqlStatement CountTenantDictTypes = new(
        "settings.count_tenant_dict_types",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_type
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListTenantDictTypesSqlServer = new(
        "settings.list_tenant_dict_types.sql_server",
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
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Name, Code, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListTenantDictTypesMySql = new(
        "settings.list_tenant_dict_types.mysql",
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
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Name, Code, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindById = new(
        "settings.tenant_dict_type.find_by_id",
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
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindIdentityById = new(
        "settings.tenant_dict_type.find_identity_by_id",
        """
        SELECT Id, Code, Name, Description, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByCode = new(
        "settings.tenant_dict_type.find_by_code",
        """
        SELECT Id, Code, Name, Description, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_type
        WHERE Code = @Code
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Insert = new(
        "settings.tenant_dict_type.insert",
        """
        INSERT INTO fn_settings_dict_type
            (Id, TenantId, Code, Name, Description, DisplayOrder, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Code, @Name, @Description, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateTenantDictType = new(
        "settings.tenant_dict_type.update",
        """
        UPDATE fn_settings_dict_type
        SET Name = @Name,
            Description = @Description,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableTenantDictType = new(
        "settings.tenant_dict_type.disable",
        """
        UPDATE fn_settings_dict_type
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountActiveItems = new(
        "settings.tenant_dict_type.count_active_items",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND type.TenantId = @TenantId
          AND item.IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>全量租户字典类型列表（不分页），供下拉与全量消费场景使用。</summary>
    public static readonly SqlStatement ListAllTenantDictTypes = new(
        "settings.tenant_dict_type.list_all",
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
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Name, Code, Id
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// 硬删除租户字典类型；前置校验 IsActive=0 与无活跃字典项由 Service 保证，
    /// WHERE 同时校验 TenantId、IsActive=0 与 Version 做租户隔离与并发控制。
    /// </summary>
    public static readonly SqlStatement DeleteTenantDictType = new(
        "settings.tenant_dict_type.delete",
        """
        DELETE FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
          AND IsActive = 0
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// 级联删除租户字典类型下的全部字典项（含已禁用），通过 JOIN 类型表校验租户边界，
    /// 在删除类型本身前于同一事务内执行。
    /// </summary>
    public static readonly SqlStatement DeleteItemsByType = new(
        "settings.tenant_dict_item.delete_by_type",
        """
        DELETE item
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND type.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
