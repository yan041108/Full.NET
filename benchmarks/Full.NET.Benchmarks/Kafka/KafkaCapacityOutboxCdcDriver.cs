using System.Collections.Concurrent;
using System.Diagnostics;
using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 创建 Scope C 的业务 Outbox、CDC Connect 与生产 Inbox Driver。
/// </summary>
public sealed class KafkaOutboxCdcScenarioDriverFactory
    : IKafkaCapacityScenarioDriverFactory
{
    public string ScopeCode => KafkaCapacityScopeCodes.TransactionOutboxCdc;

    public KafkaCapacityDriverRuntime Create(KafkaCapacityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var observer = new KafkaCapacityWorkerObserver(100_000_000);
        var provider = KafkaCapacityServiceFactory.BuildOutboxCdcServices(
            configuration,
            observer);
        var executor = new KafkaCapacityOutboxCdcExecutor(
            configuration,
            provider,
            observer,
            new KafkaCapacityOutboxProducer(),
            new KafkaCapacityConnectAdminClient(configuration.Connect));
        return new KafkaCapacityDriverRuntime(
            new KafkaOutboxCdcScenarioDriver(executor),
            executor,
            new KafkaCapacityChainedPreflight(
                new KafkaCapacityDatabasePreflight(configuration.Database, requireOutboxTable: true),
                new KafkaCapacityConnectPreflight(configuration.Connect)));
    }
}

public sealed class KafkaOutboxCdcScenarioDriver(
    KafkaCapacityOutboxCdcExecutor executor) : IKafkaCapacityScenarioDriver, IAsyncDisposable
{
    public string ScopeCode => KafkaCapacityScopeCodes.TransactionOutboxCdc;

    public Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Sample.ScopeCode, ScopeCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Scope C driver received another scope.");
        }

        return executor.ExecuteAsync(context, cancellationToken);
    }

    public ValueTask DisposeAsync() => executor.DisposeAsync();
}

/// <summary>
/// 编排 Outbox 写入、Debezium CDC、Kafka 与生产 Inbox/Handler 全链路样本。
/// </summary>
public sealed class KafkaCapacityOutboxCdcExecutor(
    KafkaCapacityConfiguration configuration,
    ServiceProvider serviceProvider,
    KafkaCapacityWorkerObserver observer,
    KafkaCapacityOutboxProducer outboxProducer,
    KafkaCapacityConnectAdminClient connectAdmin)
    : IKafkaCapacityStatisticsSource, IAsyncDisposable
{
    private readonly ConcurrentQueue<KafkaCapacityLibrdkafkaStatisticsEvidence> statistics = new();
    private string? activeConnectorName;

    public IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> SnapshotStatistics() =>
        statistics.ToArray();

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        if (context.Warmup > TimeSpan.Zero)
        {
            var warmupEvidence = await ExecuteSampleAsync(
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

        return await ExecuteSampleAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DeleteActiveConnectorAsync(CancellationToken.None).ConfigureAwait(false);
        connectAdmin.Dispose();
        serviceProvider.Dispose();
    }

    private async Task<KafkaCapacitySampleEvidence> ExecuteSampleAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        observer.BeginPhase(context.RunHash, context.SampleHash);
        var tracker = new KafkaCapacityIntegrityTracker(
            context.MaximumMessages,
            context.TopicIdentity.Partitions);
        var cdcTracker = new KafkaCapacityCdcTracker(context.MaximumMessages);
        cdcTracker.BeginPhase(context.RunHash, context.SampleHash);
        var scheduleLatency = new KafkaCapacityLatencyHistogram();
        var outboxCommitLatency = new KafkaCapacityLatencyHistogram();
        var failureCodes = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var connectorName = KafkaCapacityConnectorTemplateFactory.BuildConnectorName(
            configuration.Connect.ConnectorNamePrefix,
            context.ConsumerGroupId);
        var connectorConfig = await KafkaCapacityConnectorTemplateFactory.CreateConfigAsync(
            configuration.Database.Provider,
            configuration.Database.ConnectionString,
            configuration.Connect,
            configuration.Connect.InternalKafkaBootstrapServers
            ?? configuration.Kafka.BootstrapServers!,
            cancellationToken).ConfigureAwait(false);

        await DeleteActiveConnectorAsync(cancellationToken).ConfigureAwait(false);
        await connectAdmin.RegisterConnectorAsync(
            connectorName,
            connectorConfig,
            cancellationToken).ConfigureAwait(false);
        activeConnectorName = connectorName;
        if (!await connectAdmin.WaitForConnectorHealthyAsync(connectorName, cancellationToken)
                .ConfigureAwait(false))
        {
            return new KafkaCapacitySampleEvidence(
                context.Sample.ScopeCode,
                context.Sample.SampleId,
                context.Sample.Scenario,
                context.Sample.TargetMessagesPerSecond,
                context.Sample.PayloadSizeBytes,
                context.TopicIdentity.Partitions,
                context.Sample.ProducerConcurrency,
                KafkaCapacitySampleState.Incomplete,
                tracker.Complete(false),
                new KafkaCapacityPerformanceEvidence(
                    0,
                    0,
                    0,
                    scheduleLatency.Snapshot(),
                    outboxCommitLatency.Snapshot(),
                    observer.Snapshot().EndToEndLatency,
                    0,
                    0,
                    GC.GetTotalMemory(false),
                    0),
                ["connect_not_healthy"],
                OutboxCdc: new KafkaCapacityOutboxCdcExtensionEvidence(
                    0,
                    outboxCommitLatency.Snapshot(),
                    cdcTracker.CdcToKafkaLatency));
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

        var consumerConfig = BuildConsumerConfig(context);
        await using var consumerLoop = new KafkaCapacityWorkerConsumerLoop(
            configuration.Kafka,
            consumerConfig,
            context.TopicIdentity.TopicName,
            context.TopicIdentity.Partitions,
            serviceProvider,
            result => cdcTracker.OnKafkaMessage(result));
        var partitionSequences = new long[context.TopicIdentity.Partitions];
        var partitionLocks = Enumerable.Range(0, context.TopicIdentity.Partitions)
            .Select(static _ => new object())
            .ToArray();
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
                var partition = checked((int)(sequence % context.TopicIdentity.Partitions));
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

                tracker.OnEnqueued(sequence);
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    lock (partitionLocks[partition])
                    {
                        var partitionSequence = partitionSequences[partition]++;
                        outboxProducer.WriteCommittedAsync(
                                scope,
                                context,
                                sequence,
                                partitionSequence,
                                scheduledMessage.ScheduledTimestampMicroseconds,
                                enqueued,
                                outboxCommitLatency,
                                token)
                            .GetAwaiter()
                            .GetResult();
                        tracker.OnAcknowledged(sequence);
                        cdcTracker.NoteOutboxCommitted(
                            sequence,
                            Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failureCodes.TryAdd("outbox_commit_failed", 0);
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
        var drainStarted = Stopwatch.StartNew();
        while (drainStarted.Elapsed < context.DrainTimeout)
        {
            consumerLoop.PollAvailable(tracker, context);
            consumerLoop.ProcessCompletions(DateTimeOffset.UtcNow);
            var handler = observer.Snapshot();
            if (handler.Processed >= tracker.Complete(false).Acknowledged
                && cdcTracker.Published >= tracker.Complete(false).Acknowledged
                && consumerLoop.InFlightCount == 0)
            {
                break;
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        if (cdcTracker.Published < tracker.Complete(false).Acknowledged)
        {
            failureCodes.TryAdd("cdc_drain_timeout", 0);
        }

        await consumerLoop.StopAsync(context.DrainTimeout, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var after = WorkerResourceSnapshot.Capture();
        var handlerSnapshot = observer.Snapshot();
        var baseIntegrity = tracker.Complete(
            handlerSnapshot.Processed == tracker.Complete(false).Acknowledged
            && cdcTracker.Published == tracker.Complete(false).Acknowledged
            && consumerLoop.InFlightCount == 0);
        var integrity = baseIntegrity with
        {
            Consumed = handlerSnapshot.Processed,
            Corrupted = baseIntegrity.Corrupted + handlerSnapshot.Corrupted,
        };
        var extension = new KafkaCapacityOutboxCdcExtensionEvidence(
            cdcTracker.Published,
            outboxCommitLatency.Snapshot(),
            cdcTracker.CdcToKafkaLatency);
        var scopeCorrect = scheduling.Scheduled == integrity.Acknowledged
            && integrity.Acknowledged == extension.CdcPublished
            && extension.CdcPublished == integrity.Consumed;
        var elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000001);
        var failures = failureCodes.Keys.Order(StringComparer.Ordinal).ToArray();
        var completed = integrity.CorrectnessPassed && scopeCorrect;
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
                handlerSnapshot.Processed / elapsedSeconds,
                scheduleLatency.Snapshot(),
                outboxCommitLatency.Snapshot(),
                handlerSnapshot.EndToEndLatency,
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
                : failures.Append("scope_c_correctness_failed")
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
            OutboxCdc: extension);
    }

    private ConsumerConfig BuildConsumerConfig(KafkaCapacitySampleContext context)
    {
        var config = configuration.Kafka.BuildConsumerConfig(context.ConsumerGroupId);
        config.ClientId = context.ConsumerClientId;
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        return config;
    }

    private async Task DeleteActiveConnectorAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(activeConnectorName))
        {
            return;
        }

        await connectAdmin.DeleteConnectorAsync(activeConnectorName, cancellationToken)
            .ConfigureAwait(false);
        activeConnectorName = null;
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
