using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Features.GetDeliveryStatus;

internal sealed class DeliveryStatusQueryService(
    IOutboxBacklogReader backlogReader,
    IEffectiveEventDeliveryOwnerResolver ownerResolver,
    IEnumerable<IntegrationEventTopicDefinition> topics)
{
    private readonly IReadOnlyList<IntegrationEventTopicDefinition> _topics = topics.ToArray();

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
