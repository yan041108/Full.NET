using System.Diagnostics;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>Kafka 手工回放单条消息的处理结果；Rejected 表示契约或 Inbox 拒绝，不推进位点。</summary>
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
    Task<(string ClusterId, string TopicId)> ReadResourceIdentityAsync(string topic, CancellationToken cancellationToken);
    IReadOnlyList<TopicPartition> GetPartitions(string topic, TimeSpan timeout);

    WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout);

    IReadOnlyList<TopicPartitionOffset> OffsetsForTimes(
        IReadOnlyList<TopicPartitionTimestamp> timestamps,
        TimeSpan timeout);

    void Assign(IReadOnlyList<TopicPartitionOffset> offsets);

    void Pause(IReadOnlyList<TopicPartition> partitions);

    ConsumeResult<string, byte[]>? Consume(TimeSpan timeout);

}

/// <summary>
/// 按时间窗口从 Kafka 只读回放集成事件；经 Inbox 幂等处理，不直接调用业务 Handler。
/// </summary>
internal sealed class KafkaReplayService(
    IIntegrationEventSubscriptionCatalog catalog,
    IKafkaReplayConsumerFactory consumerFactory,
    IKafkaReplayMessageProcessor processor,
    IOptions<KafkaMessagingOptions> options) : IKafkaReplayService, ITrackedKafkaReplayService
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(200);
    private const int MaximumIdlePolls = 150;
    private readonly KafkaMessagingOptions _options = options.Value;

    public Task<KafkaReplayResult> ReplayAsync(KafkaReplayRequest request, CancellationToken cancellationToken) =>
        ReplayCoreAsync(request, null, null, cancellationToken);

    public Task<KafkaReplayResult> ReplayTrackedAsync(KafkaReplayRequest request, KafkaReplaySnapshot? snapshot,
        IKafkaReplayProgressSink progress, CancellationToken cancellationToken) =>
        ReplayCoreAsync(request, snapshot, progress ?? throw new ArgumentNullException(nameof(progress)), cancellationToken);

    private async Task<KafkaReplayResult> ReplayCoreAsync(
        KafkaReplayRequest request,
        KafkaReplaySnapshot? snapshot,
        IKafkaReplayProgressSink? progress,
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

        IReadOnlyDictionary<TopicPartition, ReplayRange> ranges;
        if (progress is null) ranges = ResolveRanges(request, consumer, selected, cancellationToken);
        else
        {
            var identity = await consumer.ReadResourceIdentityAsync(request.TopicCode, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(identity.ClusterId) || string.IsNullOrWhiteSpace(identity.TopicId))
                throw new KafkaReplayWindowUnavailableException();
            if (snapshot is null)
            {
                ranges = ResolveRanges(request, consumer, selected, cancellationToken);
                // 精确 Offset 请求不能把已经丢失或尚不存在的区间静默裁剪为成功。
                if (request.UsesOffsetRange && ranges.Values.Any(range => range.Start != request.FromOffset || range.EndExclusive != request.ToOffset + 1))
                    throw new KafkaReplayWindowUnavailableException();
                snapshot = new(identity.ClusterId, identity.TopicId, ranges.Select(item =>
                    new KafkaReplayPartitionRange(item.Key.Partition.Value, item.Value.Start, item.Value.EndExclusive, item.Value.Start)).ToArray());
            }
            else
            {
                if (snapshot.ClusterId != identity.ClusterId || snapshot.TopicId != identity.TopicId
                    || snapshot.Ranges.Count != selected.Count
                    || !selected.Select(item => item.Partition.Value).ToHashSet().SetEquals(snapshot.Ranges.Select(item => item.Partition)))
                    throw new KafkaReplayWindowUnavailableException();
                var resumed = new Dictionary<TopicPartition, ReplayRange>();
                foreach (var range in snapshot.Ranges)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var partition = new TopicPartition(request.TopicCode, range.Partition);
                    var watermark = consumer.QueryWatermarkOffsets(partition, MetadataTimeout);
                    if (range.StartOffset < 0 || range.NextOffset < range.StartOffset || range.NextOffset > range.EndOffsetExclusive
                        || range.NextOffset < watermark.Low.Value || range.EndOffsetExclusive > watermark.High.Value
                        || !resumed.TryAdd(partition, new(range.NextOffset, range.EndOffsetExclusive)))
                        throw new KafkaReplayWindowUnavailableException();
                }
                ranges = resumed;
            }
            await progress.BindSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
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
        var lastProgress = Stopwatch.GetTimestamp();
        while (remaining.Count > 0 && scanned < request.MaxMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(lastProgress) >= TimeSpan.FromSeconds(30))
                throw new TimeoutException("Kafka replay made no progress for 30 seconds.");
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

            if (!remaining.Contains(result.TopicPartition))
            {
                continue;
            }

            var endExclusive = ranges[result.TopicPartition].EndExclusive;
            if (result.IsPartitionEOF || result.Offset.Value >= endExclusive)
            {
                if (progress is not null)
                    await progress.CompletePartitionAsync(result.Partition.Value, endExclusive, cancellationToken).ConfigureAwait(false);
                remaining.Remove(result.TopicPartition);
                consumer.Pause([result.TopicPartition]);
                idlePolls = 0;
                lastProgress = Stopwatch.GetTimestamp();
                continue;
            }

            if (progress is not null)
            {
                if (result.Offset.Value < ranges[result.TopicPartition].Start) throw new KafkaReplayWindowUnavailableException();
                await progress.BeforeMessageAsync(result.Partition.Value, result.Offset.Value, cancellationToken).ConfigureAwait(false);
            }
            scanned++;
            var outcome = await processor.ProcessAsync(
                        request.ReplayConsumerName,
                        result,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (outcome is not (KafkaReplayMessageOutcome.Processed or KafkaReplayMessageOutcome.AlreadyProcessed or KafkaReplayMessageOutcome.Rejected))
                throw new InvalidOperationException("Unsupported Kafka replay message outcome.");
            if (progress is not null)
                await progress.AfterMessageAsync(result.Partition.Value, result.Offset.Value, (KafkaReplayConfirmedOutcome)outcome,
                    cancellationToken).ConfigureAwait(false);
            switch (outcome)
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

            // 运维恢复必须停在拒绝位置；不能越过缺口再声称该分区已经确认完成。
            if (progress is not null && outcome == KafkaReplayMessageOutcome.Rejected)
                return new(scanned, processed, alreadyProcessed, rejected, LimitReached: false);

            idlePolls = 0;
            lastProgress = Stopwatch.GetTimestamp();
            if (result.Offset.Value + 1 >= endExclusive)
            {
                remaining.Remove(result.TopicPartition);
                consumer.Pause([result.TopicPartition]);
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

internal sealed class KafkaReplayConsumerFactory(IOptions<KafkaMessagingOptions> options) : IKafkaReplayConsumerFactory
{
    public IKafkaReplayConsumer Create(ConsumerConfig config) =>
        new KafkaReplayConsumer(
            new ConsumerBuilder<string, byte[]>(config).Build(),
            new AdminClientBuilder(options.Value.BuildClientConfig()).Build());
}

internal sealed class KafkaReplayConsumer(
    IConsumer<string, byte[]> consumer,
    IAdminClient adminClient) : IKafkaReplayConsumer
{
    public async Task<(string ClusterId, string TopicId)> ReadResourceIdentityAsync(string topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cluster = await adminClient.DescribeClusterAsync(new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(10) }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var topics = await adminClient.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]),
            new DescribeTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var description = topics.TopicDescriptions.SingleOrDefault(item => item.Name == topic && !item.Error.IsError);
        return (cluster.ClusterId ?? string.Empty, description?.TopicId.ToString() ?? string.Empty);
    }

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

    public void Pause(IReadOnlyList<TopicPartition> partitions) => consumer.Pause(partitions);

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
