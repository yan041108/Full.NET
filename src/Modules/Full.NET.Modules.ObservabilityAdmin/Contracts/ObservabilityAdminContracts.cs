namespace Full.NET.Modules.ObservabilityAdmin.Contracts;

/// <summary>Host 日志文件控制面的精确权限码。</summary>
public static class ObservabilityLogFilePermissions
{
    public const string Read = "observability.log_files.read";

    public const string Download = "observability.log_files.download";
}

/// <summary>服务器实例目录与运行时监控的精确权限码。</summary>
public static class ObservabilityServerPermissions
{
    public const string Read = "observability.server.read";
}

/// <summary>缓存策略目录与精确失效的精确权限码。</summary>
public static class ObservabilityCachePolicyPermissions
{
    public const string Read = "observability.cache_policies.read";

    public const string Invalidate = "observability.cache_policies.invalidate";
}

/// <summary>Elasticsearch 日志管道健康检查的精确权限码。</summary>
public static class ObservabilityElasticsearchPermissions
{
    public const string Read = "observability.elasticsearch.read";
}

/// <summary>Host 日志控制面的稳定错误码。</summary>
public static class ObservabilityAdminErrorCodes
{
    public const string Prefix = "observability.";

    public const string LogFileNotFound = "observability.log_files.not_found";

    public const string ServerInstanceNotFound = "observability.server_instances.not_found";

    public const string CachePolicyNotFound = "observability.cache_policies.not_found";

    public const string CachePolicyNotInvalidatable = "observability.cache_policies.not_invalidatable";

    public const string CacheInvalidationInvalid = "observability.cache_policies.invalidation_invalid";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([
            LogFileNotFound,
            ServerInstanceNotFound,
            CachePolicyNotFound,
            CachePolicyNotInvalidatable,
            CacheInvalidationInvalid,
        ]);
}
