using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 创建 Scope B 的生产 Inbox/Dispatcher/Handler 依赖图和真实 Kafka Driver。
/// </summary>
public sealed class KafkaWorkerScenarioDriverFactory
    : IKafkaCapacityScenarioDriverFactory
{
    public string ScopeCode => KafkaCapacityScopeCodes.WorkerInboxHandler;

    public KafkaCapacityDriverRuntime Create(KafkaCapacityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var observer = new KafkaCapacityWorkerObserver(100_000_000);
        var provider = KafkaCapacityServiceFactory.BuildWorkerServices(configuration, observer);
        var executor = new KafkaCapacityWorkerExecutor(
            configuration.Kafka,
            provider,
            observer);
        return new KafkaCapacityDriverRuntime(
            new KafkaWorkerScenarioDriver(executor),
            executor,
            new KafkaCapacityDatabasePreflight(configuration.Database));
    }
}

public sealed class KafkaWorkerScenarioDriver(
    KafkaCapacityWorkerExecutor executor) : IKafkaCapacityScenarioDriver,
    IAsyncDisposable
{
    public string ScopeCode => KafkaCapacityScopeCodes.WorkerInboxHandler;

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Sample.ScopeCode, ScopeCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Scope B driver received another scope.");
        }

        return await executor.ExecuteAsync(context, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => executor.DisposeAsync();
}

/// <summary>
/// 使用生产分区 Scheduler、连续 Offset 水位和单消息处理器执行 Scope B 样本。
/// </summary>
public sealed class KafkaCapacityWorkerExecutor(
    KafkaMessagingOptions options,
    ServiceProvider serviceProvider,
    KafkaCapacityWorkerObserver observer)
    : IKafkaCapacityStatisticsSource, IAsyncDisposable
{
    private readonly ConcurrentQueue<KafkaCapacityLibrdkafkaStatisticsEvidence>
        statistics = new();

    public IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> SnapshotStatistics() =>
        statistics.ToArray();

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        if (context.Warmup > TimeSpan.Zero)
        {
            var warmupEvidence = await ExecuteAsync(
                    context.CreateWarmupPhase(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!warmupEvidence.Integrity.CorrectnessPassed
                || warmupEvidence.State != KafkaCapacitySampleState.Completed)
            {
                return warmupEvidence with
                {
                    State = KafkaCapacitySampleState.Incomplete,
                    FailureCodes = warmupEvidence.FailureCodes
                        .Append("warmup_failed")
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                };
            }
        }

        observer.BeginPhase(context.RunHash, context.SampleHash);
        var tracker = new KafkaCapacityIntegrityTracker(
            context.MaximumMessages,
            context.TopicIdentity.Partitions);
        await using var consumerLoop = new KafkaCapacityWorkerConsumerLoop(
            options,
            BuildConsumerConfig(context),
            context.TopicIdentity.TopicName,
            context.TopicIdentity.Partitions,
            serviceProvider);
        using var producer = new ProducerBuilder<string, byte[]>(
                BuildProducerConfig(context))
            .Build();
        var partitionSequences = new long[context.TopicIdentity.Partitions];
        var partitionProduceLocks = Enumerable.Range(
                0,
                context.TopicIdentity.Partitions)
            .Select(static _ => new object())
            .ToArray();
        var scheduleLatency = new KafkaCapacityLatencyHistogram();
        var acknowledgementLatency = new KafkaCapacityLatencyHistogram();
        var failureCodes = new ConcurrentDictionary<string, byte>(
            StringComparer.Ordinal);
        var before = WorkerResourceSnapshot.Capture();
        var stopwatch = Stopwatch.StartNew();
        var schedulingTask = new KafkaCapacityOpenLoopScheduler().RunAsync(
            context.Sample.TargetMessagesPerSecond,
            context.Duration,
            context.MaximumMessages,
            context.Sample.ProducerConcurrency,
            (scheduledMessage, token) =>
            {
                token.ThrowIfCancellationRequested();
                var sequence = scheduledMessage.GlobalSequence;
                var partition = checked((int)(sequence
                    % context.TopicIdentity.Partitions));
                var observedEnqueued = Stopwatch.GetElapsedTime(
                    0,
                    Stopwatch.GetTimestamp()).Ticks / 10;
                var enqueued = Math.Max(
                    scheduledMessage.ScheduledTimestampMicroseconds,
                    observedEnqueued);
                if (!scheduleLatency.RecordMicroseconds(Math.Max(
                        1,
                        enqueued - scheduledMessage.ScheduledTimestampMicroseconds)))
                {
                    failureCodes.TryAdd("schedule_latency_histogram_overflow", 0);
                }

                lock (partitionProduceLocks[partition])
                {
                    var partitionSequence = partitionSequences[partition]++;
                    var value = KafkaCapacityEnvelopeCodec.Encode(
                        context.Sample.PayloadSizeBytes,
                        context.RunHash,
                        context.SampleHash,
                        sequence,
                        partitionSequence,
                        scheduledMessage.ScheduledTimestampMicroseconds,
                        enqueued);
                    tracker.OnEnqueued(sequence);
                    producer.Produce(
                        new TopicPartition(
                            context.TopicIdentity.TopicName,
                            new Partition(partition)),
                        BuildMessage(sequence, partition, value),
                        report =>
                        {
                            if (report.Error.IsError
                                || report.Status != PersistenceStatus.Persisted)
                            {
                                failureCodes.TryAdd("delivery_not_persisted", 0);
                                return;
                            }

                            try
                            {
                                tracker.OnAcknowledged(sequence);
                                var acknowledged = Stopwatch.GetElapsedTime(
                                    0,
                                    Stopwatch.GetTimestamp()).Ticks / 10;
                                if (!acknowledgementLatency.RecordMicroseconds(
                                        Math.Max(1, acknowledged - enqueued)))
                                {
                                    failureCodes.TryAdd(
                                        "acknowledgement_latency_histogram_overflow",
                                        0);
                                }
                            }
                            catch
                            {
                                failureCodes.TryAdd("delivery_tracking_failed", 0);
                            }
                        });
                }
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        while (!schedulingTask.IsCompleted)
        {
            consumerLoop.PollAvailable(tracker, context);
            await Task.Yield();
        }

        var scheduling = await schedulingTask.ConfigureAwait(false);

        if (producer.Flush(context.DrainTimeout) != 0)
        {
            failureCodes.TryAdd("producer_flush_incomplete", 0);
        }
        var drainStarted = Stopwatch.StartNew();
        while (drainStarted.Elapsed < context.DrainTimeout)
        {
            consumerLoop.PollAvailable(tracker, context);
            consumerLoop.ProcessCompletions(DateTimeOffset.UtcNow);
            if (observer.Snapshot().Processed >= tracker.Complete(false).Acknowledged
                && consumerLoop.InFlightCount == 0)
            {
                break;
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        await consumerLoop.StopAsync(context.DrainTimeout, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var after = WorkerResourceSnapshot.Capture();

        var handler = observer.Snapshot();
        var baseIntegrity = tracker.Complete(
            handler.Processed == tracker.Complete(false).Acknowledged
            && consumerLoop.InFlightCount == 0);
        var integrity = baseIntegrity with
        {
            Consumed = handler.Processed,
            Corrupted = baseIntegrity.Corrupted + handler.Corrupted,
        };
        var elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000001);
        var failures = failureCodes.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
        var completed = integrity.CorrectnessPassed && failures.Length == 0;
        return new KafkaCapacitySampleEvidence(
            context.Sample.ScopeCode,
            context.Sample.SampleId,
            context.Sample.Scenario,
            context.Sample.TargetMessagesPerSecond,
            context.Sample.PayloadSizeBytes,
            context.TopicIdentity.Partitions,
            context.Sample.ProducerConcurrency,
            completed
                ? KafkaCapacitySampleState.Completed
                : KafkaCapacitySampleState.Incomplete,
            integrity,
            new KafkaCapacityPerformanceEvidence(
                scheduling.Scheduled / elapsedSeconds,
                integrity.Acknowledged / elapsedSeconds,
                handler.Processed / elapsedSeconds,
                scheduleLatency.Snapshot(),
                acknowledgementLatency.Snapshot(),
                handler.EndToEndLatency,
                (long)drainStarted.Elapsed.TotalMilliseconds,
                after.CpuTime <= before.CpuTime || stopwatch.Elapsed <= TimeSpan.Zero
                    ? 0
                    : (after.CpuTime - before.CpuTime).TotalMilliseconds
                        / stopwatch.Elapsed.TotalMilliseconds
                        / Math.Max(1, Environment.ProcessorCount)
                        * 100d,
                after.ManagedHeapBytes,
                consumerLoop.BufferDepth,
                after.AllocatedBytes - before.AllocatedBytes,
                after.WorkingSetBytes,
                after.Gen0Collections - before.Gen0Collections,
                after.Gen1Collections - before.Gen1Collections,
                after.Gen2Collections - before.Gen2Collections),
            completed
                ? []
                : failures.Append("scope_b_correctness_failed")
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
    }

    public ValueTask DisposeAsync()
    {
        serviceProvider.Dispose();
        return ValueTask.CompletedTask;
    }

    private ConsumerConfig BuildConsumerConfig(KafkaCapacitySampleContext context)
    {
        var config = options.BuildConsumerConfig(context.ConsumerGroupId);
        config.ClientId = context.ConsumerClientId;
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        return config;
    }

    private ProducerConfig BuildProducerConfig(KafkaCapacitySampleContext context)
    {
        var config = options.BuildProducerConfig();
        config.ClientId = context.ProducerClientId;
        return config;
    }

    private static Message<string, byte[]> BuildMessage(
        long sequence,
        int partition,
        byte[] value)
    {
        var eventId = Guid.CreateVersion7();
        var headers = new Headers
        {
            { KafkaEnvelopeHeaderNames.EventId, Encoding.UTF8.GetBytes(eventId.ToString("D")) },
            { KafkaEnvelopeHeaderNames.MessageType, Encoding.UTF8.GetBytes(KafkaCapacityWorkerContracts.EventType) },
            { KafkaEnvelopeHeaderNames.SchemaVersion, Encoding.UTF8.GetBytes("1") },
            { KafkaEnvelopeHeaderNames.ContentType, Encoding.UTF8.GetBytes(MessagingNames.ContentTypeMemoryPack) },
            { KafkaEnvelopeHeaderNames.Producer, Encoding.UTF8.GetBytes("fullnet.capacity.runner") },
            { KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) },
        };
        return new Message<string, byte[]>
        {
            Key = $"capacity-{partition}-{sequence % 64}",
            Value = value,
            Headers = headers,
        };
    }

    private sealed record WorkerResourceSnapshot(
        TimeSpan CpuTime,
        long ManagedHeapBytes,
        long AllocatedBytes,
        long WorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections)
    {
        public static WorkerResourceSnapshot Capture()
        {
            using var process = Process.GetCurrentProcess();
            return new WorkerResourceSnapshot(
                process.TotalProcessorTime,
                GC.GetTotalMemory(false),
                GC.GetTotalAllocatedBytes(precise: false),
                process.WorkingSet64,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }
}
