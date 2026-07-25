namespace Full.NET.Realtime;

/// <summary>首批稳定实时消息机器码。</summary>
public static class RealtimeMessageCodes
{
    /// <summary>Testing 探针：向当前用户回送自检消息。</summary>
    public const string ProbeSelf = "realtime.probe.self";

    /// <summary>Host 公告发布通知。</summary>
    public const string AnnouncementPublished = "notifications.announcement.published";

    /// <summary>站内信送达通知。</summary>
    public const string InboxMessageReceived = "notifications.inbox.message.received";

    /// <summary>站内信未读数变更通知。</summary>
    public const string InboxUnreadCountChanged = "notifications.inbox.unread.changed";
}
