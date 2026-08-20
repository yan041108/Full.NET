using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// Host 系统配置项 SQL 语句集合。提供配置项的分页列表、全量查询、按 Key/Id 查找、分组名去重、
/// 插入、更新、禁用、硬删除（仅禁用项）、批量硬删除、按 ConfigKey 批量更新值等语句。
/// 所有语句限定 Host 数据作用域（TenantId 为 NULL）。
/// </summary>
internal static class ConfigEntrySql
{
    public static readonly SqlStatement CountHostConfigEntries = new(
        "settings.count_host_config_entries",
        """
        SELECT COUNT(1)
        FROM fn_settings_config_entry
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostConfigEntriesSqlServer = new(
        "settings.list_host_config_entries.sql_server",
        """
        SELECT Id,
               ConfigKey,
               DisplayName,
               Description,
               GroupName,
               ValueKind,
               Value,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_config_entry
        ORDER BY DisplayOrder, DisplayName, ConfigKey, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostConfigEntriesMySql = new(
        "settings.list_host_config_entries.mysql",
        """
        SELECT Id,
               ConfigKey,
               DisplayName,
               Description,
               GroupName,
               ValueKind,
               Value,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_config_entry
        ORDER BY DisplayOrder, DisplayName, ConfigKey, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    /// <summary>全量配置项列表（不分页），对应 Admin.NET queryConfigList 全量场景。</summary>
    public static readonly SqlStatement ListAllHostConfigEntries = new(
        "settings.config_entry.list_all",
        """
        SELECT Id,
               ConfigKey,
               DisplayName,
               Description,
               GroupName,
               ValueKind,
               Value,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_config_entry
        ORDER BY GroupName, DisplayOrder, DisplayName, ConfigKey, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "settings.config_entry.find_by_id",
        """
        SELECT Id,
               ConfigKey,
               DisplayName,
               Description,
               GroupName,
               ValueKind,
               Value,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_config_entry
        WHERE Id = @ConfigEntryId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindIdentityById = new(
        "settings.config_entry.find_identity_by_id",
        """
        SELECT Id, ConfigKey, DisplayName, Description, GroupName, ValueKind, Value, DisplayOrder, IsActive, Version
        FROM fn_settings_config_entry
        WHERE Id = @ConfigEntryId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByKey = new(
        "settings.config_entry.find_by_key",
        """
        SELECT Id,
               ConfigKey,
               DisplayName,
               Description,
               GroupName,
               ValueKind,
               Value,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_config_entry
        WHERE ConfigKey = @ConfigKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindIdentityByKey = new(
        "settings.config_entry.find_identity_by_key",
        """
        SELECT Id, ConfigKey, DisplayName, Description, GroupName, ValueKind, Value, DisplayOrder, IsActive, Version
        FROM fn_settings_config_entry
        WHERE ConfigKey = @ConfigKey
        """,
        SqlDataScope.HostOnly);

    /// <summary>跨模块 Port 解析 secret 明文时使用的最小投影。</summary>
    public static readonly SqlStatement FindSecretByKey = new(
        "settings.config_entry.find_secret_by_key",
        """
        SELECT ValueKind, Value, IsActive
        FROM fn_settings_config_entry
        WHERE ConfigKey = @ConfigKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "settings.config_entry.insert",
        """
        INSERT INTO fn_settings_config_entry
            (Id, ConfigKey, DisplayName, Description, GroupName, ValueKind, Value, DisplayOrder, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, @ConfigKey, @DisplayName, @Description, @GroupName, @ValueKind, @Value, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostConfigEntry = new(
        "settings.config_entry.update",
        """
        UPDATE fn_settings_config_entry
        SET DisplayName = @DisplayName,
            Description = @Description,
            GroupName = @GroupName,
            Value = @Value,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ConfigEntryId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostConfigEntry = new(
        "settings.config_entry.disable",
        """
        UPDATE fn_settings_config_entry
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ConfigEntryId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 硬删除已禁用的配置项；WHERE 同时校验 IsActive=0 与 Version，确保删除前置条件成立。
    /// </summary>
    public static readonly SqlStatement DeleteConfigEntry = new(
        "settings.config_entry.delete",
        """
        DELETE FROM fn_settings_config_entry
        WHERE Id = @ConfigEntryId
          AND IsActive = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 批量硬删除已禁用的配置项；仅删除 IsActive=0 的行，任一目标仍启用时由 Service 前置校验拦截。
    /// </summary>
    public static readonly SqlStatement BatchDeleteConfigEntries = new(
        "settings.config_entry.batch_delete",
        """
        DELETE FROM fn_settings_config_entry
        WHERE Id IN @Ids
          AND IsActive = 0
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 ConfigKey 单行更新配置值与版本，供批量更新值在事务内逐条执行。
    /// </summary>
    public static readonly SqlStatement UpdateValueByConfigKey = new(
        "settings.config_entry.update_value_by_key",
        """
        UPDATE fn_settings_config_entry
        SET Value = @Value,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE ConfigKey = @ConfigKey
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 查询已使用分组名的去重列表，对应 Admin.NET 配置分组下拉。
    /// </summary>
    public static readonly SqlStatement ListGroups = new(
        "settings.config_entry.list_groups",
        """
        SELECT DISTINCT GroupName
        FROM fn_settings_config_entry
        WHERE GroupName IS NOT NULL
          AND GroupName <> ''
        ORDER BY GroupName
        """,
        SqlDataScope.HostOnly);
}
