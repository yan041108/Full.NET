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

    public const string Retract = "notifications.announcements.retract";

    /// <summary>查看我收到的 Host 公告列表、详情与未读计数。</summary>
    public const string ReceivedRead = "notifications.announcements.received.read";

    /// <summary>将单条收到的 Host 公告标记为已读。</summary>
    public const string ReceivedMarkRead = "notifications.announcements.received.mark_read";

    /// <summary>将全部可见 Host 公告标记为已读。</summary>
    public const string ReceivedMarkAllRead = "notifications.announcements.received.mark_all_read";

    /// <summary>查看 Host 公告阅读统计与已读明细。</summary>
    public const string ReadStats = "notifications.announcements.read_stats";
}
