namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Host 公告相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class HostAnnouncementPermissions
{
    /// <summary>允许查看 Host 公告列表、详情与过滤条件中的公告元数据。</summary>
    public const string Read = "notifications.announcements.read";

    /// <summary>允许创建新的 Host 公告草稿。</summary>
    public const string Create = "notifications.announcements.create";

    /// <summary>允许更新处于 Draft 状态的 Host 公告内容与受众。</summary>
    public const string Update = "notifications.announcements.update";

    /// <summary>允许将 Draft 状态的 Host 公告发布为 Published 状态。</summary>
    public const string Publish = "notifications.announcements.publish";

    /// <summary>允许将 Published 状态的 Host 公告撤回为 Retracted 状态。</summary>
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
