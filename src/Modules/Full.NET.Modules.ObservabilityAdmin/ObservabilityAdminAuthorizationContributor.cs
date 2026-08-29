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
    ];
}
