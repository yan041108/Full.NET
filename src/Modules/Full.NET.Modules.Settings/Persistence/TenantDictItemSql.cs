using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// 租户数据字典项 SQL 语句集合。提供租户下字典项按类型计数、分页列表、按 Id 查找、
/// 插入、更新、禁用、硬删除等语句。通过 INNER JOIN 字典类型表确保租户隔离，
/// 所有语句绑定当前租户上下文（TenantRequired）。
/// </summary>
internal static class TenantDictItemSql
{
    public static readonly SqlStatement CountByTypeId = new(
        "settings.tenant_dict_item.count_by_type_id",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND type.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListByTypeIdSqlServer = new(
        "settings.tenant_dict_item.list_by_type_id.sql_server",
        """
        SELECT item.Id,
               item.DictTypeId,
               item.Label,
               item.Value,
               item.Color,
               item.DisplayOrder,
               item.IsActive,
               item.CreatedAtUtc,
               item.UpdatedAtUtc,
               item.Version
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND type.TenantId = @TenantId
        ORDER BY item.DisplayOrder, item.Label, item.Value, item.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListByTypeIdMySql = new(
        "settings.tenant_dict_item.list_by_type_id.mysql",
        """
        SELECT item.Id,
               item.DictTypeId,
               item.Label,
               item.Value,
               item.Color,
               item.DisplayOrder,
               item.IsActive,
               item.CreatedAtUtc,
               item.UpdatedAtUtc,
               item.Version
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND type.TenantId = @TenantId
        ORDER BY item.DisplayOrder, item.Label, item.Value, item.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindById = new(
        "settings.tenant_dict_item.find_by_id",
        """
        SELECT item.Id,
               item.DictTypeId,
               item.Label,
               item.Value,
               item.Color,
               item.DisplayOrder,
               item.IsActive,
               item.CreatedAtUtc,
               item.UpdatedAtUtc,
               item.Version
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindIdentityById = new(
        "settings.tenant_dict_item.find_identity_by_id",
        """
        SELECT item.Id, item.DictTypeId, item.Label, item.Value, item.Color,
               item.DisplayOrder, item.IsActive, item.Version
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByTypeAndValue = new(
        "settings.tenant_dict_item.find_by_type_and_value",
        """
        SELECT item.Id, item.DictTypeId, item.Label, item.Value, item.Color,
               item.DisplayOrder, item.IsActive, item.Version
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND item.Value = @Value
          AND type.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Insert = new(
        "settings.tenant_dict_item.insert",
        """
        INSERT INTO fn_settings_dict_item
            (Id, DictTypeId, Label, Value, Color, DisplayOrder, IsActive, CreatedAtUtc, Version)
        SELECT
            @Id, @DictTypeId, @Label, @Value, @Color, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version
        FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateTenantDictItem = new(
        "settings.tenant_dict_item.update",
        """
        UPDATE item
        SET Label = @Label,
            Color = @Color,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = item.Version + 1
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
          AND item.Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateTenantDictItemMySql = new(
        "settings.tenant_dict_item.update.mysql",
        """
        UPDATE fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        SET item.Label = @Label,
            item.Color = @Color,
            item.DisplayOrder = @DisplayOrder,
            item.UpdatedAtUtc = @UpdatedAtUtc,
            item.Version = item.Version + 1
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
          AND item.Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableTenantDictItem = new(
        "settings.tenant_dict_item.disable",
        """
        UPDATE item
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = item.Version + 1
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
          AND item.IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableTenantDictItemMySql = new(
        "settings.tenant_dict_item.disable.mysql",
        """
        UPDATE fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        SET item.IsActive = 0,
            item.UpdatedAtUtc = @UpdatedAtUtc,
            item.Version = item.Version + 1
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
          AND item.IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// 按租户字典类型编码查询启用字典项，对应 Admin.NET dataList by code。
    /// JOIN fn_settings_dict_type 按 Code 与 TenantId 过滤，仅返回 IsActive=1 的项。
    /// </summary>
    public static readonly SqlStatement ListByTypeCode = new(
        "settings.tenant_dict_item.list_by_type_code",
        """
        SELECT item.Id,
               item.DictTypeId,
               item.Label,
               item.Value,
               item.Color,
               item.DisplayOrder,
               item.IsActive,
               item.CreatedAtUtc,
               item.UpdatedAtUtc,
               item.Version
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE type.Code = @Code
          AND type.TenantId = @TenantId
          AND item.IsActive = 1
        ORDER BY item.DisplayOrder, item.Label, item.Value, item.Id
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// 硬删除租户字典项；前置校验 IsActive=0 由 Service 保证，
    /// 通过 JOIN 类型表校验租户边界，WHERE 同时校验 IsActive=0 与 Version。
    /// </summary>
    public static readonly SqlStatement DeleteTenantDictItem = new(
        "settings.tenant_dict_item.delete",
        """
        DELETE item
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.Id = @DictItemId
          AND type.TenantId = @TenantId
          AND item.IsActive = 0
          AND item.Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
