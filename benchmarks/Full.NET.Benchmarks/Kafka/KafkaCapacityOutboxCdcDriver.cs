using System.Collections.Concurrent;
using System.Diagnostics;
using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Kafka;
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
            KafkaCapacityConnectClients.Create(configuration.Connect));
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
    KafkaConnectAdminClient connectAdmin)
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

    /// <summary>
    /// 执行单个 Scope C 样本，并在开始计量前等待 Connector 建立可观测的源位点。
    /// </summary>
    /// <param name="context">当前容量样本上下文。</param>
    /// <param name="cancellationToken">用于取消 Connector 准备、生产与排空的令牌。</param>
    /// <returns>包含完整性、性能与 CDC 扩展数据的样本证据。</returns>
    private async Task<KafkaCapacitySampleEvidence> ExecuteSampleAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        var tracker = new KafkaCapacityIntegrityTracker(
            context.MaximumMessages,
            context.TopicIdentity.Partitions);
        var cdcTracker = new KafkaCapacityCdcTracker(context.MaximumMessages);
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
        if (!await connectAdmin.WaitForConnectorHealthyAsync(
                connectorName,
                TimeSpan.FromSeconds(configuration.Connect.HealthTimeoutSeconds),
                cancellationToken)
                .ConfigureAwait(false))
        {
            return CreateConnectorUnavailableEvidence(
                context,
                tracker,
                scheduleLatency,
                outboxCommitLatency,
                cdcTracker,
                "connect_not_healthy");
        }

        // RUNNING 只表示 Task 线程已启动；必须等待源位点可读，否则 schema snapshot 仍可锁表并丢失样本起点。
        if (!await WaitForConnectorPositionAsync(
                connectorName,
                TimeSpan.FromSeconds(configuration.Connect.HealthTimeoutSeconds),
                cancellationToken).ConfigureAwait(false))
        {
            return CreateConnectorUnavailableEvidence(
                context,
                tracker,
                scheduleLatency,
                outboxCommitLatency,
                cdcTracker,
                "connect_position_not_ready");
        }


        // 控制面 RUNNING 与 offset 只能证明 Source Task 已启动；真实 Outbox 探针必须先穿过 CDC 路由，
        // 否则正式样本可能落在 schema 初始化与 binlog 流切换之间并被错误判为业务丢失。
        if (!await WaitForCdcDeliveryAsync(
                context,
                TimeSpan.FromSeconds(configuration.Connect.HealthTimeoutSeconds),
                cancellationToken).ConfigureAwait(false))
        {
            return CreateConnectorUnavailableEvidence(
                context,
                tracker,
                scheduleLatency,
                outboxCommitLatency,
                cdcTracker,
                "connect_delivery_not_ready");
        }

        observer.BeginPhase(context.RunHash, context.SampleHash);
        cdcTracker.BeginPhase(context.RunHash, context.SampleHash);

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
            .Select(static _ => new SemaphoreSlim(1, 1))
            .ToArray();
        var before = WorkerResourceSnapshot.Capture();
        var stopwatch = Stopwatch.StartNew();
        var schedulingTask = new KafkaCapacityOpenLoopScheduler().RunAsync(
            context.Sample.TargetMessagesPerSecond,
            context.Duration,
            context.MaximumMessages,
            context.Sample.ProducerConcurrency,
            async (scheduledMessage, token) =>
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
                await partitionLocks[partition].WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await using var scope = serviceProvider.CreateAsyncScope();
                    var partitionSequence = partitionSequences[partition]++;
                    await outboxProducer.WriteCommittedAsync(
                            scope,
                            context,
                            sequence,
                            partitionSequence,
                            scheduledMessage.ScheduledTimestampMicroseconds,
                            enqueued,
                            outboxCommitLatency,
                            token)
                        .ConfigureAwait(false);
                    tracker.OnAcknowledged(sequence);
                    cdcTracker.NoteOutboxCommitted(
                        sequence,
                        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failureCodes.TryAdd("outbox_commit_failed", 0);
                }
                finally
                {
                    partitionLocks[partition].Release();
                }
            },
            cancellationToken);

        while (!schedulingTask.IsCompleted)
        {
            consumerLoop.PollAvailable(tracker, context);
            await Task.Yield();
        }

        var scheduling = await schedulingTask.ConfigureAwait(false);
        foreach (var partitionLock in partitionLocks)
        {
            partitionLock.Dispose();
        }
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

    /// <summary>
    /// 轮询 Connector 已提交的源位点，作为 snapshot 完成且可安全开始计量的语义门禁。
    /// </summary>
    /// <param name="connectorName">当前样本的 Connector 名称。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="cancellationToken">用于取消轮询的令牌。</param>
    /// <returns>在时限内读到源位点时返回 <see langword="true"/>。</returns>
    private async Task<bool> WaitForConnectorPositionAsync(
        string connectorName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await connectAdmin.TryReadConnectorPositionAsync(
                    connectorName,
                    cancellationToken).ConfigureAwait(false) is not null)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// 写入隔离的 Outbox 探针并等待 Debezium 将其路由到正式 Topic，确认数据面已真正就绪。
    /// </summary>
    /// <param name="context">正式样本上下文，用于派生隔离探针与目标 Topic。</param>
    /// <param name="timeout">探针从数据库提交到 Kafka 可见的最长等待时间。</param>
    /// <param name="cancellationToken">用于取消数据库写入和 Kafka 轮询的令牌。</param>
    /// <returns>在时限内读取到精确探针载荷时返回真。</returns>
    private async Task<bool> WaitForCdcDeliveryAsync(
        KafkaCapacitySampleContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var probeContext = context.CreateCdcReadinessProbe();
        using var consumer = new ConsumerBuilder<string, byte[]>(
            BuildConsumerConfig(probeContext)).Build();
        var assignments = Enumerable.Range(0, context.TopicIdentity.Partitions)
            .Select(partition => new TopicPartitionOffset(
                context.TopicIdentity.TopicName,
                new Partition(partition),
                consumer.QueryWatermarkOffsets(
                    new TopicPartition(
                        context.TopicIdentity.TopicName,
                        new Partition(partition)),
                    timeout).High))
            .ToArray();
        consumer.Assign(assignments);

        // 探针使用独立哈希且正式 Consumer 在探针完成后才读取高水位，因此不会污染吞吐与完整性统计。
        var probeTimestamp = Stopwatch.GetElapsedTime(
            0,
            Stopwatch.GetTimestamp()).Ticks / 10;
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            await outboxProducer.WriteCommittedAsync(
                    scope,
                    probeContext,
                    0,
                    0,
                    probeTimestamp,
                    probeTimestamp,
                    new KafkaCapacityLatencyHistogram(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            var consumed = consumer.Consume(
                remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250));
            if (consumed?.Message?.Value is null
                || !KafkaCapacityEnvelopePayloadDecoder.TryDecode(
                    consumed.Message.Value,
                    out var envelope))
            {
                continue;
            }

            if (envelope.RunHash == probeContext.RunHash
                && envelope.SampleHash == probeContext.SampleHash
                && envelope.GlobalSequence == 0)
            {
                consumer.Close();
                return true;
            }
        }

        consumer.Close();
        return false;
    }

    /// <summary>
    /// 创建 Connector 未就绪时的统一未完成证据，确保不把空样本误报为性能结果。
    /// </summary>
    /// <param name="context">当前样本上下文。</param>
    /// <param name="tracker">完整性跟踪器。</param>
    /// <param name="scheduleLatency">调度延迟直方图。</param>
    /// <param name="outboxCommitLatency">Outbox 提交延迟直方图。</param>
    /// <param name="cdcTracker">CDC 发布跟踪器。</param>
    /// <param name="failureCode">稳定失败代码。</param>
    /// <returns>不含计量样本的未完成证据。</returns>
    private KafkaCapacitySampleEvidence CreateConnectorUnavailableEvidence(
        KafkaCapacitySampleContext context,
        KafkaCapacityIntegrityTracker tracker,
        KafkaCapacityLatencyHistogram scheduleLatency,
        KafkaCapacityLatencyHistogram outboxCommitLatency,
        KafkaCapacityCdcTracker cdcTracker,
        string failureCode) =>
        new(
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
            [failureCode],
            OutboxCdc: new KafkaCapacityOutboxCdcExtensionEvidence(
                0,
                outboxCommitLatency.Snapshot(),
                cdcTracker.CdcToKafkaLatency));

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

/// <summary>
/// 容量 Runner 对 BuildingBlocks <see cref="KafkaConnectAdminClient"/> 的薄工厂。
/// </summary>
internal static class KafkaCapacityConnectClients
{
    public static KafkaConnectAdminClient Create(KafkaCapacityConnectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Uri.TryCreate(configuration.BaseUri, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidDataException("Connect BaseUri is invalid.");
        }

        return new KafkaConnectAdminClient(
            baseUri,
            TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
    }
}
