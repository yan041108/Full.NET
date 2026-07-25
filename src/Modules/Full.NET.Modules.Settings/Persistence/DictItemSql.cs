using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

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
}
