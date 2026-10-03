namespace Full.NET.Hosting.Observability;

/// <summary>宿主冻结日志索引路由所需的版本与事件保留期限。</summary>
internal sealed class LogIndexRoutingPolicy
{
    public LogIndexRoutingPolicy(int version, int retentionDays)
    {
        if (version is < 1 or > 9999 || retentionDays is < 1 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(version),
                "Index route version and retention days must be within their supported ranges.");
        }

        Version = version;
        RetentionDays = retentionDays;
    }

    public int Version { get; }

    public int RetentionDays { get; }

    public static LogIndexRoutingPolicy? FromOptions(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.IndexRouteVersion == 0 && options.IndexRetentionDays == 0)
        {
            return null;
        }

        return new LogIndexRoutingPolicy(options.IndexRouteVersion, options.IndexRetentionDays);
    }
}
