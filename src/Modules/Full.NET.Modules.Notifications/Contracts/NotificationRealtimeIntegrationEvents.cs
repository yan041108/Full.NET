using global::MemoryPack;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>定义 Notifications 实时修复事件的稳定消息类型。</summary>
public static class NotificationRealtimeEventTypes
{
    /// <summary>Host 公告已经发布。</summary>
    public const string AnnouncementPublished =
        "fullnet.notifications.announcement.published";

    /// <summary>Host 站内信已经送达。</summary>
    public const string InboxMessageReceived =
        "fullnet.notifications.inbox.received";

    /// <summary>Host 站内信已读状态已经变更。</summary>
    public const string InboxReadStateChanged =
        "fullnet.notifications.inbox.read_state_changed";
}

/// <summary>表示 Host 公告发布事实已经与业务状态原子提交。</summary>
/// <param name="AnnouncementId">已发布公告的稳定标识。</param>
/// <param name="Title">供实时客户端刷新前展示的公告标题。</param>
[MemoryPackable]
public partial record AnnouncementPublishedIntegrationEvent(
    Guid AnnouncementId,
    string Title);

/// <summary>表示一条 Host 站内信已经与业务状态原子提交。</summary>
/// <param name="RecipientUserId">接收站内信的 Host 用户标识。</param>
/// <param name="MessageId">已送达站内信的稳定标识。</param>
/// <param name="Title">供实时客户端刷新前展示的站内信标题。</param>
[MemoryPackable]
public partial record InboxMessageReceivedIntegrationEvent(
    Guid RecipientUserId,
    Guid MessageId,
    string Title);

/// <summary>表示指定 Host 用户的站内信已读状态已经提交变更。</summary>
/// <param name="RecipientUserId">需要重新读取未读数的 Host 用户标识。</param>
[MemoryPackable]
public partial record InboxReadStateChangedIntegrationEvent(
    Guid RecipientUserId);
