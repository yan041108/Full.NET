using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications;

/// <summary>
/// 向授权目录贡献 Notifications 模块的权限、导航与操作定义。
/// </summary>
/// <remarks>
/// 公告与站内信操作均归属 Host 作用域；每个受保护操作绑定独立稳定权限码，
/// 客户端可见性仅负责体验，服务端 Endpoint 仍按精确权限重新校验。
/// </remarks>
internal sealed class NotificationsAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("notifications", "通知中心", 60);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            HostAnnouncementPermissions.Read,
            "查询公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Create,
            "创建公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Update,
            "更新公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Publish,
            "发布公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.Read,
            "查询站内信",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.Send,
            "发送站内信",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.MarkRead,
            "标记站内信已读",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.MarkAllRead,
            "全部标记站内信已读",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-announcements",
            null,
            "host-announcements",
            "/notifications/host-announcements",
            "host-announcements",
            "公告管理",
            "Announcements",
            "bell",
            55,
            HostAnnouncementPermissions.Read),
        new NavigationDefinition(
            "inbox-messages",
            null,
            "inbox-messages",
            "/notifications/inbox-messages",
            "inbox-messages",
            "消息中心",
            "Inbox",
            "message",
            56,
            InboxPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "notifications.announcements.create",
            "host-announcements",
            HostAnnouncementPermissions.Create,
            "创建公告",
            "create",
            10),
        new AuthorizationActionDefinition(
            "notifications.announcements.update",
            "host-announcements",
            HostAnnouncementPermissions.Update,
            "编辑公告",
            "update",
            20),
        new AuthorizationActionDefinition(
            "notifications.announcements.publish",
            "host-announcements",
            HostAnnouncementPermissions.Publish,
            "发布公告",
            "publish",
            30),
        new AuthorizationActionDefinition(
            "notifications.inbox.send",
            "inbox-messages",
            InboxPermissions.Send,
            "发送站内信",
            "send",
            10),
        new AuthorizationActionDefinition(
            "notifications.inbox.mark_read",
            "inbox-messages",
            InboxPermissions.MarkRead,
            "标记已读",
            "mark_read",
            20),
        new AuthorizationActionDefinition(
            "notifications.inbox.mark_all_read",
            "inbox-messages",
            InboxPermissions.MarkAllRead,
            "全部标记已读",
            "mark_all_read",
            30),
    ];
}
