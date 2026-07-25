using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

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

    public static readonly SqlStatement FindById = new(
        "settings.config_entry.find_by_id",
        """
        SELECT Id,
               ConfigKey,
               DisplayName,
               Description,
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
        SELECT Id, ConfigKey, DisplayName, Description, ValueKind, Value, DisplayOrder, IsActive, Version
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
        SELECT Id, ConfigKey, DisplayName, Description, ValueKind, Value, DisplayOrder, IsActive, Version
        FROM fn_settings_config_entry
        WHERE ConfigKey = @ConfigKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "settings.config_entry.insert",
        """
        INSERT INTO fn_settings_config_entry
            (Id, ConfigKey, DisplayName, Description, ValueKind, Value, DisplayOrder, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, @ConfigKey, @DisplayName, @Description, @ValueKind, @Value, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostConfigEntry = new(
        "settings.config_entry.update",
        """
        UPDATE fn_settings_config_entry
        SET DisplayName = @DisplayName,
            Description = @Description,
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
}
