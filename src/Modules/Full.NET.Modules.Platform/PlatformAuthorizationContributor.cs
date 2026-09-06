using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Platform.Contracts;

namespace Full.NET.Modules.Platform;

/// <summary>
/// 向授权目录贡献 Platform 模块的权限、导航与操作定义。
/// </summary>
internal sealed class PlatformAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    private const AuthorizationScope PlatformScopes =
        AuthorizationScope.Host | AuthorizationScope.Tenant;

    /// <summary>Platform 模块在授权目录中的定义。</summary>
    public AuthorizationModuleDefinition Module { get; } =
        new("platform", "平台", 64);

    /// <summary>Platform 模块全部权限定义。</summary>
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            PlatformPermissions.Read,
            "查询更新日志",
            PlatformScopes),
        new PermissionDefinition(
            PlatformPermissions.Create,
            "创建更新日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.Update,
            "更新更新日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.Publish,
            "发布更新日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.Retract,
            "撤回更新日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.Delete,
            "删除更新日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.MarkRead,
            "标记更新日志已读",
            PlatformScopes),
        new PermissionDefinition(
            PlatformPermissions.BackupTasksRead,
            "查询授权备份任务目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.BackupRunsRead,
            "查询授权备份运行结果",
            AuthorizationScope.Host),
        new PermissionDefinition(
            PlatformPermissions.BackupRunsDownload,
            "下载授权备份产物",
            AuthorizationScope.Host),
    ];

    /// <summary>Platform 模块导航定义。</summary>
    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-release-notes",
            null,
            "host-release-notes",
            "/platform/host-release-notes",
            "host-release-notes",
            "更新日志",
            "Release Notes",
            "platform",
            65,
            PlatformPermissions.Read),
        new NavigationDefinition(
            "my-release-notes",
            null,
            "my-release-notes",
            "/platform/my-release-notes",
            "my-release-notes",
            "我的更新",
            "My Release Notes",
            "platform",
            66,
            PlatformPermissions.Read),
        new NavigationDefinition(
            "backup-executor",
            null,
            "backup-executor",
            "/platform/backup-executor",
            "backup-executor",
            "授权备份",
            "Authorized Backup",
            "platform",
            67,
            PlatformPermissions.BackupTasksRead),
    ];

    /// <summary>Platform 模块页面操作定义。</summary>
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "platform.release_notes.create",
            "host-release-notes",
            PlatformPermissions.Create,
            "创建更新日志",
            "create",
            10),
        new AuthorizationActionDefinition(
            "platform.release_notes.update",
            "host-release-notes",
            PlatformPermissions.Update,
            "编辑更新日志",
            "update",
            20),
        new AuthorizationActionDefinition(
            "platform.release_notes.publish",
            "host-release-notes",
            PlatformPermissions.Publish,
            "发布更新日志",
            "publish",
            30),
        new AuthorizationActionDefinition(
            "platform.release_notes.retract",
            "host-release-notes",
            PlatformPermissions.Retract,
            "撤回更新日志",
            "retract",
            40),
        new AuthorizationActionDefinition(
            "platform.release_notes.delete",
            "host-release-notes",
            PlatformPermissions.Delete,
            "删除更新日志",
            "delete",
            50),
        new AuthorizationActionDefinition(
            "platform.release_notes.mark_read",
            "my-release-notes",
            PlatformPermissions.MarkRead,
            "标记已读",
            "mark-read",
            10),
    ];
}
