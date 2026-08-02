using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Files;

internal sealed class FilesAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("files", "文件管理", 50);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            HostFilePermissions.Read,
            "查询文件",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFilePermissions.Upload,
            "上传文件",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFilePermissions.Download,
            "下载文件",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFilePermissions.Delete,
            "删除文件",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-files",
            null,
            "host-files",
            "/files/host-files",
            "host-files",
            "文件管理",
            "Files",
            "folder",
            70,
            HostFilePermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "files.files.upload",
            "host-files",
            HostFilePermissions.Upload,
            "上传文件",
            "upload",
            10),
        new AuthorizationActionDefinition(
            "files.files.download",
            "host-files",
            HostFilePermissions.Download,
            "下载文件",
            "download",
            20),
        new AuthorizationActionDefinition(
            "files.files.delete",
            "host-files",
            HostFilePermissions.Delete,
            "删除文件",
            "delete",
            30),
    ];
}
