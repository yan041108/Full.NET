using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Features.GetDeliveryStatus;

/// <summary>
/// 查询事件交付状态总览：汇总 Outbox 积压并解析各事件流当前生效的交付所有者。
/// </summary>
/// <remarks>
/// 所有者解析经 <see cref="EffectiveEventDeliveryOwnerResolver"/>，反映切流后的真实链路；
/// 事件流按事件类型与版本稳定排序，保证运维视图顺序稳定。
/// </remarks>
internal sealed class DeliveryStatusQueryService(
    IOutboxBacklogReader backlogReader,
    IEffectiveEventDeliveryOwnerResolver ownerResolver,
    IEnumerable<IntegrationEventTopicDefinition> topics)
{
    private readonly IReadOnlyList<IntegrationEventTopicDefinition> _topics = topics.ToArray();

    /// <summary>
    /// 返回当前 Outbox 积压摘要与各事件流交付所有者总览。
    /// </summary>
    public async Task<Result<DeliveryStatusResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var backlog = await backlogReader.ReadBacklogAsync(cancellationToken).ConfigureAwait(false);
        var streams = new List<EventStreamStatusResponse>(_topics.Count);
        foreach (var topic in _topics.OrderBy(t => t.EventType, StringComparer.Ordinal)
            .ThenBy(t => t.SchemaVersion))
        {
            var owner = await ownerResolver
                .GetDeliveryOwnerAsync(topic.EventType, topic.SchemaVersion, cancellationToken)
                .ConfigureAwait(false);
            streams.Add(new EventStreamStatusResponse(
                topic.EventType,
                topic.SchemaVersion,
                topic.TopicCode,
                owner));
        }

        return Result<DeliveryStatusResponse>.Success(
            new DeliveryStatusResponse(
                new OutboxBacklogSummaryResponse(
                    backlog.PendingCount,
                    backlog.DueRetryCount,
                    backlog.ActiveLeaseCount,
                    backlog.DeadLetterCount,
                    backlog.OldestOccurredAtUtc,
                    backlog.OldestDeadLetteredAtUtc),
                streams));
    }
}
