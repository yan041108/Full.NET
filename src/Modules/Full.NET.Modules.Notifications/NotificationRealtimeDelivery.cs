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

    public async Task PublishInboxUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        // 延迟或并发的 Outbox 事件可能乱序，消费时读取当前值才能让徽标收敛到数据库事实。
        var unreadCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                new
                {
                    RecipientUserId = recipientUserId,
                    UnreadStatus = InboxMessageStatuses.Unread,
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
