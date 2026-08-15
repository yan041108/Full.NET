using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// Host 数据字典项 SQL 语句集合。提供字典项下按类型计数、分页列表、按 Id 查找、
/// 插入、更新、禁用、硬删除等语句。所有语句通过 DictTypeId 与 Host 字典类型关联，
/// 限定 Host 数据作用域（TenantId 为 NULL）。
/// </summary>
internal static class DictItemSql
{
    public static readonly SqlStatement CountByTypeId = new(
        "settings.dict_item.count_by_type_id",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_item
        WHERE DictTypeId = @DictTypeId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListByTypeIdSqlServer = new(
        "settings.dict_item.list_by_type_id.sql_server",
        """
        SELECT Id,
               DictTypeId,
               Label,
               Value,
               Color,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_item
        WHERE DictTypeId = @DictTypeId
        ORDER BY DisplayOrder, Label, Value, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListByTypeIdMySql = new(
        "settings.dict_item.list_by_type_id.mysql",
        """
        SELECT Id,
               DictTypeId,
               Label,
               Value,
               Color,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_item
        WHERE DictTypeId = @DictTypeId
        ORDER BY DisplayOrder, Label, Value, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "settings.dict_item.find_by_id",
        """
        SELECT Id,
               DictTypeId,
               Label,
               Value,
               Color,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_item
        WHERE Id = @DictItemId
        """,
        SqlDataScope.HostOnly);

    // 管理路径只需要乐观锁与状态字段；避免把时间戳列映射进 Identity 记录。
    public static readonly SqlStatement FindIdentityById = new(
        "settings.dict_item.find_identity_by_id",
        """
        SELECT Id, DictTypeId, Label, Value, Color, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_item
        WHERE Id = @DictItemId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByTypeAndValue = new(
        "settings.dict_item.find_by_type_and_value",
        """
        SELECT Id, DictTypeId, Label, Value, Color, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_item
        WHERE DictTypeId = @DictTypeId
          AND Value = @Value
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "settings.dict_item.insert",
        """
        INSERT INTO fn_settings_dict_item
            (Id, DictTypeId, Label, Value, Color, DisplayOrder, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, @DictTypeId, @Label, @Value, @Color, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostDictItem = new(
        "settings.dict_item.update",
        """
        UPDATE fn_settings_dict_item
        SET Label = @Label,
            Color = @Color,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictItemId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostDictItem = new(
        "settings.dict_item.disable",
        """
        UPDATE fn_settings_dict_item
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictItemId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 Host 字典类型编码查询启用字典项，对应 Admin.NET dataList by code。
    /// JOIN fn_settings_dict_type 按 Code 过滤，仅返回 IsActive=1 的项。
    /// </summary>
    public static readonly SqlStatement ListByTypeCode = new(
        "settings.dict_item.list_by_type_code",
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
          AND type.TenantId IS NULL
          AND item.IsActive = 1
        ORDER BY item.DisplayOrder, item.Label, item.Value, item.Id
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 硬删除 Host 字典项；前置校验 IsActive=0 由 Service 保证，
    /// WHERE 同时校验 IsActive=0 与 Version 做防御性兜底与并发控制。
    /// </summary>
    public static readonly SqlStatement DeleteDictItem = new(
        "settings.dict_item.delete",
        """
        DELETE FROM fn_settings_dict_item
        WHERE Id = @DictItemId
          AND IsActive = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}
