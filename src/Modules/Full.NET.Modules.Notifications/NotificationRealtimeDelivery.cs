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
    public Task PublishAnnouncementAsync(
        AnnouncementPublishedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        realtimePublisher.PublishToHostBroadcastAsync(
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
    /// SignalR 只携带低敏刷新提示；未读数始终按事件中的受信 TenantScopeKey 重新读取数据库。
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
                integrationEvent.TenantScopeKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 重新读取指定用户在指定作用域的当前未读站内信数量并推送给该用户。
    /// </summary>
    public async Task PublishInboxUnreadCountAsync(
        Guid recipientUserId,
        string tenantScopeKey,
        CancellationToken cancellationToken)
    {
        var unreadCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                NotificationPlatformSqlParameters.Create(
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", tenantScopeKey),
                    ("UnreadStatus", InboxMessageStatuses.Unread)),
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
