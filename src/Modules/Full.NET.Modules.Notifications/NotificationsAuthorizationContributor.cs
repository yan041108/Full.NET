using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications;

internal sealed class NotificationsAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            HostAnnouncementPermissions.Read,
            "查询公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Write,
            "管理公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.Read,
            "查询站内信",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.Write,
            "发送站内信",
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
}
