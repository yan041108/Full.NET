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
        new PermissionDefinition(
            HostFilePermissions.Update,
            "更新文件元数据",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFilePermissions.ReferencesRead,
            "查询文件引用",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFolderPermissions.Create,
            "创建虚拟目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFolderPermissions.Update,
            "更新虚拟目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostFolderPermissions.Delete,
            "删除虚拟目录",
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
            "files.files.update",
            "host-files",
            HostFilePermissions.Update,
            "编辑元数据",
            "edit",
            25),
        new AuthorizationActionDefinition(
            "files.file_references.read",
            "host-files",
            HostFilePermissions.ReferencesRead,
            "查看引用",
            "references",
            28),
        new AuthorizationActionDefinition(
            "files.files.delete",
            "host-files",
            HostFilePermissions.Delete,
            "删除文件",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "files.folders.create",
            "host-files",
            HostFolderPermissions.Create,
            "创建目录",
            "folder-create",
            40),
        new AuthorizationActionDefinition(
            "files.folders.update",
            "host-files",
            HostFolderPermissions.Update,
            "更新目录",
            "folder-update",
            50),
        new AuthorizationActionDefinition(
            "files.folders.delete",
            "host-files",
            HostFolderPermissions.Delete,
            "删除目录",
            "folder-delete",
            60),
    ];
}
