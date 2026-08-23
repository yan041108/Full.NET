namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Host 公告相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class HostAnnouncementPermissions
{
    public const string Read = "notifications.announcements.read";

    public const string Create = "notifications.announcements.create";

    public const string Update = "notifications.announcements.update";

    public const string Publish = "notifications.announcements.publish";
}
