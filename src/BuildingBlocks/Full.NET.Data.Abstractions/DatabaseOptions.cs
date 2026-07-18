namespace Full.NET.Data.Abstractions;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; }

    public string ConnectionName { get; set; } = "fullnet";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 MySQL UUID 的物理存储模式；迁移完成前默认保持旧 CHAR(36) 映射。
    /// </summary>
    public MySqlGuidStorageMode MySqlGuidStorageMode { get; set; } =
        global::Full.NET.Data.Abstractions.MySqlGuidStorageMode.LegacyChar36;

    public int CommandTimeoutSeconds { get; set; } = 30;
}
