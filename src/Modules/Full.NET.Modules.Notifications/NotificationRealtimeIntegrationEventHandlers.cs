using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications;

/// <summary>消费已提交的公告发布事实并修复 Host 实时广播。</summary>
internal sealed class AnnouncementPublishedRealtimeHandler(
    IIntegrationEventSerializer serializer,
    NotificationRealtimeDelivery delivery) : IIntegrationEventHandler
{
    public string EventType => NotificationRealtimeEventTypes.AnnouncementPublished;

    public int SchemaVersion => 1;

    // 客户端只按稳定机器码刷新公告目录，重复广播不会产生新的业务状态。
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        delivery.PublishAnnouncementAsync(
            serializer.Deserialize<AnnouncementPublishedIntegrationEvent>(payload),
            cancellationToken);
}

/// <summary>消费已提交的站内信送达事实并修复用户实时消息与未读数。</summary>
internal sealed class InboxMessageReceivedRealtimeHandler(
    IIntegrationEventSerializer serializer,
    NotificationRealtimeDelivery delivery) : IIntegrationEventHandler
{
    public string EventType => NotificationRealtimeEventTypes.InboxMessageReceived;

    public int SchemaVersion => 1;

    // 重复投递只触发同一消息目录刷新，未读数始终重新读取当前数据库状态。
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        delivery.PublishInboxMessageAsync(
            serializer.Deserialize<InboxMessageReceivedIntegrationEvent>(payload),
            cancellationToken);
}

/// <summary>消费已提交的已读状态变更并修复用户当前未读数。</summary>
internal sealed class InboxReadStateChangedRealtimeHandler(
    IIntegrationEventSerializer serializer,
    NotificationRealtimeDelivery delivery) : IIntegrationEventHandler
{
    public string EventType => NotificationRealtimeEventTypes.InboxReadStateChanged;

    public int SchemaVersion => 1;

    // 重复投递会重新发布相同的当前计数，不会写入任何业务状态。
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent =
            serializer.Deserialize<InboxReadStateChangedIntegrationEvent>(payload);
        return delivery.PublishInboxUnreadCountAsync(
            integrationEvent.RecipientUserId,
            cancellationToken);
    }
}
