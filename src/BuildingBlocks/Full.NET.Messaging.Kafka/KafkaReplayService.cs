using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

internal enum KafkaReplayMessageOutcome
{
    Processed = 0,
    AlreadyProcessed = 1,
    Rejected = 2,
}

internal interface IKafkaReplayMessageProcessor
{
    Task<KafkaReplayMessageOutcome> ProcessAsync(
        string consumerName,
        ConsumeResult<string, byte[]> consumeResult,
        CancellationToken cancellationToken);
}

internal interface IKafkaReplayConsumerFactory
{
    IKafkaReplayConsumer Create(ConsumerConfig config);
}

internal interface IKafkaReplayConsumer : IAsyncDisposable
{
    IReadOnlyList<TopicPartition> GetPartitions(string topic, TimeSpan timeout);

    WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout);

    IReadOnlyList<TopicPartitionOffset> OffsetsForTimes(
        IReadOnlyList<TopicPartitionTimestamp> timestamps,
        TimeSpan timeout);

    void Assign(IReadOnlyList<TopicPartitionOffset> offsets);

    ConsumeResult<string, byte[]>? Consume(TimeSpan timeout);

}

internal sealed class KafkaReplayService(
    IIntegrationEventSubscriptionCatalog catalog,
    IKafkaReplayConsumerFactory consumerFactory,
    IKafkaReplayMessageProcessor processor,
    IOptions<KafkaMessagingOptions> options) : IKafkaReplayService
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(200);
    private const int MaximumIdlePolls = 150;
    private readonly KafkaMessagingOptions _options = options.Value;

    public async Task<KafkaReplayResult> ReplayAsync(
        KafkaReplayRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _ = catalog.GetTopicByCodeRequired(request.TopicCode);

        var config = _options.BuildConsumerConfig(
            $"{request.ReplayConsumerName}.replay.{Guid.NewGuid():N}");
        config.ClientId = $"{_options.ClientId}.replay";
        config.GroupInstanceId = null;
        config.EnablePartitionEof = true;
        config.AutoOffsetReset = AutoOffsetReset.Error;

        await using var consumer = consumerFactory.Create(config);
        cancellationToken.ThrowIfCancellationRequested();
        var available = consumer.GetPartitions(request.TopicCode, MetadataTimeout);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = SelectPartitions(request, available);
        if (selected.Count > KafkaReplayRequest.MaximumPartitions)
        {
            throw new ArgumentException(
                $"Kafka replay is limited to {KafkaReplayRequest.MaximumPartitions} partitions per operation.",
                nameof(request));
        }

        var ranges = ResolveRanges(request, consumer, selected, cancellationToken);
        var starts = ranges
            .Where(range => range.Value.Start < range.Value.EndExclusive)
            .Select(range => new TopicPartitionOffset(range.Key, range.Value.Start))
            .ToArray();
        if (starts.Length == 0)
        {
            return new KafkaReplayResult(0, 0, 0, 0, LimitReached: false);
        }

        consumer.Assign(starts);
        var remaining = starts.Select(start => start.TopicPartition).ToHashSet();
        var scanned = 0;
        var processed = 0;
        var alreadyProcessed = 0;
        var rejected = 0;
        var idlePolls = 0;
        while (remaining.Count > 0 && scanned < request.MaxMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = consumer.Consume(PollTimeout);
            if (result is null)
            {
                if (++idlePolls >= MaximumIdlePolls)
                {
                    throw new TimeoutException(
                        "Kafka replay made no progress for 30 seconds before reaching its fixed end offsets.");
                }

                continue;
            }

            idlePolls = 0;
            if (!remaining.Contains(result.TopicPartition))
            {
                continue;
            }

            var endExclusive = ranges[result.TopicPartition].EndExclusive;
            if (result.IsPartitionEOF || result.Offset.Value >= endExclusive)
            {
                remaining.Remove(result.TopicPartition);
                continue;
            }

            scanned++;
            switch (await processor.ProcessAsync(
                        request.ReplayConsumerName,
                        result,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                case KafkaReplayMessageOutcome.Processed:
                    processed++;
                    break;
                case KafkaReplayMessageOutcome.AlreadyProcessed:
                    alreadyProcessed++;
                    break;
                case KafkaReplayMessageOutcome.Rejected:
                    rejected++;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported Kafka replay message outcome.");
            }

            if (result.Offset.Value + 1 >= endExclusive)
            {
                remaining.Remove(result.TopicPartition);
            }
        }

        return new KafkaReplayResult(
            scanned,
            processed,
            alreadyProcessed,
            rejected,
            LimitReached: remaining.Count > 0 && scanned >= request.MaxMessages);
    }

    private static IReadOnlyList<TopicPartition> SelectPartitions(
        KafkaReplayRequest request,
        IReadOnlyList<TopicPartition> available)
    {
        if (request.Partitions.Count == 0)
        {
            return available;
        }

        var byNumber = available.ToDictionary(partition => partition.Partition.Value);
        var selected = new List<TopicPartition>(request.Partitions.Count);
        foreach (var partition in request.Partitions)
        {
            if (!byNumber.TryGetValue(partition, out var topicPartition))
            {
                throw new ArgumentException(
                    $"Partition {partition} is not present in topic '{request.TopicCode}'.",
                    nameof(request));
            }

            selected.Add(topicPartition);
        }

        return selected;
    }

    private static IReadOnlyDictionary<TopicPartition, ReplayRange> ResolveRanges(
        KafkaReplayRequest request,
        IKafkaReplayConsumer consumer,
        IReadOnlyList<TopicPartition> partitions,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<TopicPartition, TopicPartitionOffset>? fromTimes = null;
        IReadOnlyDictionary<TopicPartition, TopicPartitionOffset>? toTimes = null;
        if (request.UsesTimeRange)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fromTimes = consumer.OffsetsForTimes(
                    partitions.Select(partition => new TopicPartitionTimestamp(
                        partition,
                        new Timestamp(request.FromTimestampUtc!.Value.UtcDateTime)))
                    .ToArray(),
                    MetadataTimeout)
                .ToDictionary(offset => offset.TopicPartition);
            cancellationToken.ThrowIfCancellationRequested();
            toTimes = consumer.OffsetsForTimes(
                    partitions.Select(partition => new TopicPartitionTimestamp(
                        partition,
                        new Timestamp(request.ToTimestampUtc!.Value.UtcDateTime)))
                    .ToArray(),
                    MetadataTimeout)
                .ToDictionary(offset => offset.TopicPartition);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var ranges = new Dictionary<TopicPartition, ReplayRange>();
        foreach (var partition in partitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var watermark = consumer.QueryWatermarkOffsets(partition, MetadataTimeout);
            cancellationToken.ThrowIfCancellationRequested();
            var start = request.UsesOffsetRange
                ? request.FromOffset!.Value
                : ResolveTimestampOffset(fromTimes![partition], watermark.High.Value);
            var endExclusive = request.UsesOffsetRange
                ? checked(request.ToOffset!.Value + 1)
                : ResolveTimestampOffset(toTimes![partition], watermark.High.Value);
            ranges.Add(
                partition,
                new ReplayRange(
                    Math.Clamp(start, watermark.Low.Value, watermark.High.Value),
                    Math.Clamp(endExclusive, watermark.Low.Value, watermark.High.Value)));
        }

        return ranges;
    }

    private static long ResolveTimestampOffset(
        TopicPartitionOffset offset,
        long highWatermark) =>
        offset.Offset == Offset.Unset ? highWatermark : offset.Offset.Value;

    private readonly record struct ReplayRange(long Start, long EndExclusive);
}

internal sealed class KafkaReplayConsumerFactory : IKafkaReplayConsumerFactory
{
    public IKafkaReplayConsumer Create(ConsumerConfig config) =>
        new KafkaReplayConsumer(
            new ConsumerBuilder<string, byte[]>(config).Build(),
            new AdminClientBuilder(config).Build());
}

internal sealed class KafkaReplayConsumer(
    IConsumer<string, byte[]> consumer,
    IAdminClient adminClient) : IKafkaReplayConsumer
{
    public IReadOnlyList<TopicPartition> GetPartitions(string topic, TimeSpan timeout)
    {
        var metadata = adminClient.GetMetadata(topic, timeout);
        var topicMetadata = metadata.Topics.SingleOrDefault(item =>
            string.Equals(item.Topic, topic, StringComparison.Ordinal));
        if (topicMetadata is null || topicMetadata.Error.IsError)
        {
            throw new KafkaException(
                topicMetadata?.Error ?? new Error(ErrorCode.UnknownTopicOrPart));
        }

        return topicMetadata.Partitions
            .Select(partition => new TopicPartition(topic, partition.PartitionId))
            .ToArray();
    }

    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout) =>
        consumer.QueryWatermarkOffsets(partition, timeout);

    public IReadOnlyList<TopicPartitionOffset> OffsetsForTimes(
        IReadOnlyList<TopicPartitionTimestamp> timestamps,
        TimeSpan timeout) =>
        consumer.OffsetsForTimes(timestamps, timeout);

    public void Assign(IReadOnlyList<TopicPartitionOffset> offsets) => consumer.Assign(offsets);

    public ConsumeResult<string, byte[]>? Consume(TimeSpan timeout) => consumer.Consume(timeout);

    public ValueTask DisposeAsync()
    {
        try
        {
            // 范围重放使用显式 Assign 和唯一临时 Group，不需要执行可能阻塞的 Group Leave/Close。
            consumer.Unassign();
        }
        finally
        {
            consumer.Dispose();
            adminClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class KafkaReplayMessageProcessor(
    IServiceScopeFactory scopeFactory,
    KafkaEnvelopeReader envelopeReader) : IKafkaReplayMessageProcessor
{
    public async Task<KafkaReplayMessageOutcome> ProcessAsync(
        string consumerName,
        ConsumeResult<string, byte[]> consumeResult,
        CancellationToken cancellationToken)
    {
        if (!envelopeReader.TryRead(consumeResult, out var envelope, out _))
        {
            return KafkaReplayMessageOutcome.Rejected;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
            var subscription = KafkaConsumerWorker.ResolveSubscription(
                scope.ServiceProvider,
                catalog,
                consumerName,
                envelope!.MessageType,
                envelope.SchemaVersion);
            var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventConsumerDispatcher>();
            var result = await dispatcher.ConsumeAsync(
                    consumerName,
                    envelope,
                    subscription,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Status == InboxConsumeStatus.AlreadyProcessed
                ? KafkaReplayMessageOutcome.AlreadyProcessed
                : KafkaReplayMessageOutcome.Processed;
        }
        catch (IntegrationEventPermanentException)
        {
            return KafkaReplayMessageOutcome.Rejected;
        }
    }
}
