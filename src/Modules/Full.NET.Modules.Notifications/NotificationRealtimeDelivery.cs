using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;

namespace Full.NET.Modules.Notifications;

/// <summary>
/// 统一构造 Notifications 实时消息；调用方决定直接推送失败是降级还是触发 Outbox 重试。
/// </summary>
internal sealed class NotificationRealtimeDelivery(
    IQueryExecutor queryExecutor,
    IRealtimePublisher realtimePublisher)
{
    /// <summary>
    /// 向 Host 广播组发布已提交的公告，供客户端刷新公告目录。
    /// </summary>
    /// <remarks>
    /// 直接推送失败时调用方决定降级或由 Outbox 修复；本方法不保证恰好一次投递，
    /// 客户端按稳定机器码幂等刷新，重复广播不产生新的业务状态。
    /// </remarks>
    public Task PublishAnnouncementAsync(
        AnnouncementPublishedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        realtimePublisher.PublishToGroupAsync(
            RealtimeGroups.HostBroadcast,
            new RealtimeMessage(
                RealtimeMessageCodes.AnnouncementPublished,
                new Dictionary<string, object?>
                {
                    ["announcementId"] = integrationEvent.AnnouncementId,
                    ["title"] = integrationEvent.Title,
                }),
            cancellationToken);

    /// <summary>
    /// 向指定用户推送已送达的站内信，并同步刷新其未读数徽标。
    /// </summary>
    /// <remarks>
    /// 推送只反映已提交事实，不参与写入；重复投递只触发同一目录刷新，
    /// 未读数始终由 <see cref="PublishInboxUnreadCountAsync"/> 重新读取当前数据库值。
    /// </remarks>
    public async Task PublishInboxMessageAsync(
        InboxMessageReceivedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await realtimePublisher.PublishToUserAsync(
                integrationEvent.RecipientUserId,
                new RealtimeMessage(
                    RealtimeMessageCodes.InboxMessageReceived,
                    new Dictionary<string, object?>
                    {
                        ["messageId"] = integrationEvent.MessageId,
                        ["title"] = integrationEvent.Title,
                    }),
                cancellationToken)
            .ConfigureAwait(false);
        await PublishInboxUnreadCountAsync(
                integrationEvent.RecipientUserId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 重新读取指定用户的当前未读站内信数量并推送给该用户。
    /// </summary>
    /// <remarks>
    /// 延迟或并发的 Outbox 事件可能乱序到达，徽标必须以数据库当前值为准才能收敛；
    /// 因此每次都查询权威值而不是在客户端累加，重复调用幂等且不写入业务状态。
    /// </remarks>
    public async Task PublishInboxUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        // 延迟或并发的 Outbox 事件可能乱序，消费时读取当前值才能让徽标收敛到数据库事实。
        var unreadCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                new Dictionary<string, object?>
                {
                    ["RecipientUserId"] = recipientUserId,
                    ["UnreadStatus"] = InboxMessageStatuses.Unread,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await realtimePublisher.PublishToUserAsync(
                recipientUserId,
                new RealtimeMessage(
                    RealtimeMessageCodes.InboxUnreadCountChanged,
                    new Dictionary<string, object?>
                    {
                        ["unreadCount"] = unreadCount,
                    }),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
