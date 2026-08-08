using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Features.GetDeliveryStatus;

internal sealed class DeliveryStatusQueryService(
    IOutboxBacklogReader backlogReader,
    IntegrationEventSubscriptionCatalog catalog,
    IEnumerable<IntegrationEventTopicDefinition> topics)
{
    private readonly IReadOnlyList<IntegrationEventTopicDefinition> _topics = topics.ToArray();

    public async Task<Result<DeliveryStatusResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var backlog = await backlogReader.ReadBacklogAsync(cancellationToken).ConfigureAwait(false);
        var streams = _topics
            .Select(topic => new EventStreamStatusResponse(
                topic.EventType,
                topic.SchemaVersion,
                topic.TopicCode,
                catalog.GetDeliveryOwner(topic.EventType, topic.SchemaVersion)))
            .OrderBy(stream => stream.EventType, StringComparer.Ordinal)
            .ThenBy(stream => stream.SchemaVersion)
            .ToArray();

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
