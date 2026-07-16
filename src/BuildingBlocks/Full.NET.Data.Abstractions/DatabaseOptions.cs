namespace Full.NET.Data.Abstractions;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; }

    public string ConnectionName { get; set; } = "fullnet";

    public string ConnectionString { get; set; } = string.Empty;

    public int CommandTimeoutSeconds { get; set; } = 30;
}
