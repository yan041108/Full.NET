using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Contracts;

namespace Full.NET.Modules.ObservabilityAdmin;

internal sealed class ObservabilityAdminAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("observability", "可观测性", 85);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            ObservabilityLogFilePermissions.Read,
            "读取 Host 日志文件列表与有界尾部内容",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ObservabilityLogFilePermissions.Download,
            "下载 Host 日志文件",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ObservabilityServerPermissions.Read,
            "读取服务器实例目录与运行时监控信息",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ObservabilityCachePolicyPermissions.Read,
            "读取已登记缓存策略目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ObservabilityCachePolicyPermissions.Invalidate,
            "执行已登记缓存精确失效操作",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ObservabilityElasticsearchPermissions.Read,
            "读取 Elasticsearch 日志管道健康状态",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "observability-log-files",
            null,
            "observability-log-files",
            "/observability/log-files",
            "observability-log-files",
            "运行日志",
            "Runtime Logs",
            "monitor",
            80,
            ObservabilityLogFilePermissions.Read),
        new NavigationDefinition(
            "observability-server-monitor",
            null,
            "observability-server-monitor",
            "/observability/server-monitor",
            "observability-server-monitor",
            "服务器监控",
            "Server Monitor",
            "monitor",
            70,
            ObservabilityServerPermissions.Read),
        new NavigationDefinition(
            "observability-cache-policies",
            null,
            "observability-cache-policies",
            "/observability/cache-policies",
            "observability-cache-policies",
            "缓存管理",
            "Cache Policies",
            "monitor",
            60,
            ObservabilityCachePolicyPermissions.Read),
        new NavigationDefinition(
            "observability-elasticsearch-health",
            null,
            "observability-elasticsearch-health",
            "/observability/elasticsearch-health",
            "observability-elasticsearch-health",
            "Elasticsearch 日志",
            "Elasticsearch Logs",
            "monitor",
            55,
            ObservabilityElasticsearchPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "observability.log_files.download",
            "observability-log-files",
            ObservabilityLogFilePermissions.Download,
            "下载日志",
            "download",
            10),
        new AuthorizationActionDefinition(
            "observability.cache_policies.invalidate",
            "observability-cache-policies",
            ObservabilityCachePolicyPermissions.Invalidate,
            "失效缓存",
            "invalidate",
            10),
    ];
}
