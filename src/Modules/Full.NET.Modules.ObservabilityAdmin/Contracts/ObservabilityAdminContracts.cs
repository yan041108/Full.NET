namespace Full.NET.Modules.ObservabilityAdmin.Contracts;

/// <summary>Host 日志文件控制面的精确权限码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class ObservabilityLogFilePermissions
{
    /// <summary>允许查询日志文件目录与摘要。</summary>
    public const string Read = "observability.log_files.read";

    /// <summary>允许下载指定日志文件内容。</summary>
    public const string Download = "observability.log_files.download";
}

/// <summary>服务器实例目录与运行时监控的精确权限码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class ObservabilityServerPermissions
{
    /// <summary>允许查询服务器实例目录与运行时指标。</summary>
    public const string Read = "observability.server.read";
}

/// <summary>缓存策略目录与精确失效的精确权限码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class ObservabilityCachePolicyPermissions
{
    /// <summary>允许查询缓存策略目录与命中统计。</summary>
    public const string Read = "observability.cache_policies.read";

    /// <summary>允许按策略或键精确失效缓存。</summary>
    public const string Invalidate = "observability.cache_policies.invalidate";
}

/// <summary>Elasticsearch 日志管道健康检查的精确权限码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class ObservabilityElasticsearchPermissions
{
    /// <summary>允许查询 Elasticsearch 日志管道健康状态。</summary>
    public const string Read = "observability.elasticsearch.read";
}

/// <summary>Host 日志控制面的稳定错误码。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class ObservabilityAdminErrorCodes
{
    /// <summary>可观测性模块错误码统一前缀；所有稳定错误码均以此前缀开头。</summary>
    public const string Prefix = "observability.";

    /// <summary>指定的日志文件不存在；调用方应检查文件路径与实例标识。</summary>
    public const string LogFileNotFound = "observability.log_files.not_found";

    /// <summary>指定的服务器实例不存在；调用方应检查实例标识。</summary>
    public const string ServerInstanceNotFound = "observability.server_instances.not_found";

    /// <summary>指定的缓存策略不存在；调用方应检查策略键。</summary>
    public const string CachePolicyNotFound = "observability.cache_policies.not_found";

    /// <summary>缓存策略不允许失效；调用方应确认策略是否支持手动失效。</summary>
    public const string CachePolicyNotInvalidatable = "observability.cache_policies.not_invalidatable";

    /// <summary>缓存失效请求无效；调用方应按契约修正策略或键参数。</summary>
    public const string CacheInvalidationInvalid = "observability.cache_policies.invalidation_invalid";

    /// <summary>获取 ObservabilityAdmin 模块所有稳定错误码的只读列表，用于校验或枚举。</summary>
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([
            LogFileNotFound,
            ServerInstanceNotFound,
            CachePolicyNotFound,
            CachePolicyNotInvalidatable,
            CacheInvalidationInvalid,
        ]);
}
