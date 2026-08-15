using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// 用户网格偏好 SQL 语句集合。提供按用户与 GridKey 查找、插入、当前 Schema 更新、
/// 过期 Schema 替换、删除等语句。支持 HybridCache 二级缓存读写，数据作用域为全局（按 UserId 天然隔离）。
/// </summary>
internal static class GridPreferenceSql
{
    public static readonly SqlStatement FindByUserAndGrid = new(
        "settings.grid_preference.find_by_verified_user_and_grid",
        """
        SELECT Id, UserId, GridKey, SchemaVersion, ColumnsJson,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_settings_user_grid_preference
        WHERE UserId = @UserId
          AND GridKey = @GridKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "settings.grid_preference.insert_for_verified_user",
        """
        INSERT INTO fn_settings_user_grid_preference
            (Id, UserId, GridKey, SchemaVersion, ColumnsJson,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @UserId, @GridKey, @SchemaVersion, @ColumnsJson,
             @CreatedAtUtc, NULL, @Version)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateCurrentSchema = new(
        "settings.grid_preference.update_for_verified_user",
        """
        UPDATE fn_settings_user_grid_preference
        SET ColumnsJson = @ColumnsJson,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE UserId = @UserId
          AND GridKey = @GridKey
          AND SchemaVersion = @SchemaVersion
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ReplaceStaleSchema = new(
        "settings.grid_preference.replace_stale_schema_for_verified_user",
        """
        UPDATE fn_settings_user_grid_preference
        SET SchemaVersion = @SchemaVersion,
            ColumnsJson = @ColumnsJson,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE UserId = @UserId
          AND GridKey = @GridKey
          AND SchemaVersion <> @SchemaVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Delete = new(
        "settings.grid_preference.delete_for_verified_user",
        """
        DELETE FROM fn_settings_user_grid_preference
        WHERE UserId = @UserId
          AND GridKey = @GridKey
        """,
        SqlDataScope.Global);
}

internal sealed class GridPreferenceRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string GridKey { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string ColumnsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
