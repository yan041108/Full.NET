using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 运行独立 Producer/Consumer 传输样本并形成固定内存正确性与性能证据。
/// </summary>
public sealed class KafkaCapacityTransportExecutor : IKafkaCapacityTransportExecutor,
    IKafkaCapacityStatisticsSource
{
    private const uint WarmupHashMask = 0xA5A5_A5A5;
    private readonly KafkaMessagingOptions options;
    private readonly IKafkaCapacityProducerFactory producerFactory;
    private readonly IKafkaCapacityConsumerFactory consumerFactory;
    private readonly IKafkaCapacityWorkloadScheduler scheduler;
    private readonly IKafkaCapacityClock clock;
    private readonly ConcurrentQueue<KafkaCapacityLibrdkafkaStatisticsEvidence>
        statistics = new();

    public KafkaCapacityTransportExecutor(
        KafkaMessagingOptions options,
        IKafkaCapacityProducerFactory producerFactory,
        IKafkaCapacityConsumerFactory consumerFactory)
        : this(
            options,
            producerFactory,
            consumerFactory,
            new KafkaCapacityOpenLoopScheduler(),
            SystemKafkaCapacityClock.Instance)
    {
    }

    internal KafkaCapacityTransportExecutor(
        KafkaMessagingOptions options,
        IKafkaCapacityProducerFactory producerFactory,
        IKafkaCapacityConsumerFactory consumerFactory,
        IKafkaCapacityWorkloadScheduler scheduler,
        IKafkaCapacityClock clock)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.producerFactory = producerFactory
            ?? throw new ArgumentNullException(nameof(producerFactory));
        this.consumerFactory = consumerFactory
            ?? throw new ArgumentNullException(nameof(consumerFactory));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> SnapshotStatistics() =>
        statistics.ToArray();

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        statistics.Clear();
        var statisticsHandler = new Action<string>(OnStatistics);
        await using var producer = producerFactory.Create(options, statisticsHandler);
        await using var consumer = consumerFactory.Create(
            options,
            context.ConsumerGroupId,
            statisticsHandler);
        PhaseState? activePhase = null;
        await consumer.StartAsync(
            context.TopicIdentity.TopicName,
            (message, _) =>
            {
                var phase = Volatile.Read(ref activePhase);
                phase?.OnConsumed(message, clock.GetTimestampMicroseconds());
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        using (var assignmentTimeout =
               CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            assignmentTimeout.CancelAfter(
                TimeSpan.FromMilliseconds(options.DeliveryTimeoutMilliseconds));
            await consumer.WaitForAssignmentAsync(assignmentTimeout.Token);
        }

        var finalState = new PhaseState(context, context.SampleHash);
        var finalScheduling = EmptySchedulingResult("not_started");
        var before = new ResourceSnapshot();
        var after = new ResourceSnapshot();
        var elapsed = TimeSpan.Zero;
        long drainMilliseconds = 0;
        var drainCompleted = false;
        var warmupFailed = false;
        try
        {
            if (context.Warmup > TimeSpan.Zero)
            {
                var warmupState = new PhaseState(
                    context,
                    context.SampleHash ^ WarmupHashMask);
                Volatile.Write(ref activePhase, warmupState);
                var warmupResult = await RunPhaseAsync(
                    context,
                    context.Warmup,
                    Math.Min(
                        context.MaximumMessages,
                        Math.Max(
                            1,
                            checked((int)Math.Ceiling(
                                context.Sample.TargetMessagesPerSecond
                                * context.Warmup.TotalSeconds)))),
                    warmupState,
                    producer,
                    consumer.Completion,
                    cancellationToken);
                if (!warmupResult.Evidence.CorrectnessPassed
                    || warmupResult.FailureCodes.Count > 0)
                {
                    var failedState = new PhaseState(context, context.SampleHash);
                    failedState.AddFailure("warmup_failed");
                    finalState = failedState;
                    finalScheduling = EmptySchedulingResult("warmup_failed");
                    drainMilliseconds = warmupResult.DrainMilliseconds;
                    warmupFailed = true;
                }
            }

            if (!warmupFailed)
            {
                Volatile.Write(ref activePhase, finalState);
                before = ResourceSnapshot.Capture();
                var executionStopwatch = Stopwatch.StartNew();
                var productionResult = await RunPhaseAsync(
                    context,
                    context.Duration,
                    context.MaximumMessages,
                    finalState,
                    producer,
                    consumer.Completion,
                    cancellationToken);
                executionStopwatch.Stop();
                after = ResourceSnapshot.Capture();
                elapsed = executionStopwatch.Elapsed;
                finalScheduling = productionResult.Scheduling;
                drainMilliseconds = productionResult.DrainMilliseconds;
                drainCompleted = productionResult.DrainCompleted;
            }
        }
        finally
        {
            Volatile.Write(ref activePhase, null);
            using var stopTimeout = new CancellationTokenSource(context.DrainTimeout);
            try
            {
                await consumer.StopAsync(stopTimeout.Token);
            }
            catch
            {
                finalState.AddFailure("consumer_close_failed");
                drainCompleted = false;
            }
        }

        return BuildEvidence(
            context,
            finalState,
            finalScheduling,
            elapsed,
            before,
            after,
            drainMilliseconds,
            drainCompleted);
    }

    private async Task<PhaseResult> RunPhaseAsync(
        KafkaCapacitySampleContext context,
        TimeSpan duration,
        int maximumMessages,
        PhaseState state,
        IKafkaCapacityProducer producer,
        Task consumerCompletion,
        CancellationToken cancellationToken)
    {
        using var phaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var laneCount = Math.Min(
            context.Sample.ProducerConcurrency,
            context.TopicIdentity.Partitions);
        var totalLaneCapacity = Math.Min(
            maximumMessages,
            Math.Min(
                1_000_000,
                Math.Max(1_024, context.Sample.ProducerConcurrency * 4_096)));
        var perLaneCapacity = Math.Max(1, totalLaneCapacity / laneCount);
        var lanes = Enumerable.Range(0, laneCount)
            .Select(_ => Channel.CreateBounded<KafkaCapacityScheduledMessage>(
                new BoundedChannelOptions(perLaneCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                }))
            .ToArray();
        var partitionSequences = new long[context.TopicIdentity.Partitions];
        var laneTasks = lanes.Select((lane, laneIndex) => RunLaneAsync(
                lane.Reader,
                laneIndex,
                laneCount,
                context,
                state,
                partitionSequences,
                producer,
                phaseCancellation))
            .ToArray();

        KafkaCapacitySchedulingResult scheduling;
        try
        {
            var schedulingTask = scheduler.RunAsync(
                context.Sample.TargetMessagesPerSecond,
                duration,
                maximumMessages,
                context.Sample.ProducerConcurrency,
                async (message, token) =>
                {
                    var partition = checked((int)(message.GlobalSequence
                        % context.TopicIdentity.Partitions));
                    var lane = partition % laneCount;
                    await lanes[lane].Writer.WriteAsync(message, token);
                },
                phaseCancellation.Token);
            var completed = await Task.WhenAny(schedulingTask, consumerCompletion);
            if (completed == consumerCompletion)
            {
                state.AddFailure("consumer_stopped");
                phaseCancellation.Cancel();
            }

            scheduling = await schedulingTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state.AddFailure("cancelled");
            scheduling = EmptySchedulingResult("cancelled");
        }
        catch (OperationCanceledException)
        {
            scheduling = EmptySchedulingResult("transport_cancelled");
        }
        catch
        {
            state.AddFailure("scheduler_failed");
            phaseCancellation.Cancel();
            scheduling = EmptySchedulingResult("scheduler_failed");
        }
        finally
        {
            foreach (var lane in lanes)
            {
                lane.Writer.TryComplete();
            }
        }

        try
        {
            await Task.WhenAll(laneTasks);
        }
        catch (OperationCanceledException)
        {
            // 取消后丢弃尚未进入 Producer 的 Lane 项，已入队项仍由 Flush 排空。
        }
        catch
        {
            state.AddFailure("producer_lane_failed");
        }

        if (scheduling.StopReasonCode is not null)
        {
            state.AddFailure(scheduling.StopReasonCode);
        }

        var drainStopwatch = Stopwatch.StartNew();
        var flushRemaining = producer.Flush(context.DrainTimeout);
        if (flushRemaining != 0)
        {
            state.AddFailure("producer_flush_incomplete");
        }

        var drainCompleted = false;
        while (drainStopwatch.Elapsed < context.DrainTimeout)
        {
            var current = state.Tracker.Complete(drainCompleted: false);
            if (flushRemaining == 0
                && current.Enqueued == current.Acknowledged
                && current.Acknowledged == current.Consumed)
            {
                drainCompleted = true;
                break;
            }

            if (state.HasTerminalDeliveryFailure)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        drainStopwatch.Stop();
        return new PhaseResult(
            state,
            scheduling,
            state.Tracker.Complete(drainCompleted),
            state.FailureCodes,
            drainStopwatch.ElapsedMilliseconds,
            drainCompleted);
    }

    private async Task RunLaneAsync(
        ChannelReader<KafkaCapacityScheduledMessage> reader,
        int laneIndex,
        int laneCount,
        KafkaCapacitySampleContext context,
        PhaseState state,
        long[] partitionSequences,
        IKafkaCapacityProducer producer,
        CancellationTokenSource phaseCancellation)
    {
        await foreach (var scheduled in reader.ReadAllAsync(
                           phaseCancellation.Token))
        {
            var partition = checked((int)(scheduled.GlobalSequence
                % context.TopicIdentity.Partitions));
            if (partition % laneCount != laneIndex)
            {
                state.AddFailure("partition_lane_mismatch");
                phaseCancellation.Cancel();
                return;
            }

            var partitionSequence = partitionSequences[partition]++;
            var enqueuedTimestamp = clock.GetTimestampMicroseconds();
            byte[] value;
            try
            {
                value = KafkaCapacityEnvelopeCodec.Encode(
                    context.Sample.PayloadSizeBytes,
                    context.RunHash,
                    state.SampleHash,
                    scheduled.GlobalSequence,
                    partitionSequence,
                    scheduled.ScheduledTimestampMicroseconds,
                    enqueuedTimestamp);
                state.Tracker.OnEnqueued(scheduled.GlobalSequence);
                state.ScheduleLatency.RecordMicroseconds(Math.Max(
                    1,
                    enqueuedTimestamp - scheduled.ScheduledTimestampMicroseconds));
                producer.Produce(
                    context.TopicIdentity.TopicName,
                    partition,
                    $"partition-{partition}",
                    value,
                    scheduled.GlobalSequence,
                    report => state.OnDelivery(
                        report,
                        enqueuedTimestamp,
                        clock.GetTimestampMicroseconds(),
                        phaseCancellation));
            }
            catch
            {
                state.AddFailure("produce_failed");
                phaseCancellation.Cancel();
                throw;
            }
        }
    }

    private KafkaCapacitySampleEvidence BuildEvidence(
        KafkaCapacitySampleContext context,
        PhaseState state,
        KafkaCapacitySchedulingResult scheduling,
        TimeSpan elapsed,
        ResourceSnapshot before,
        ResourceSnapshot after,
        long drainMilliseconds,
        bool drainCompleted)
    {
        var integrity = state.Tracker.Complete(drainCompleted);
        var denominator = Math.Max(0.001d, context.Duration.TotalSeconds);
        var failures = state.FailureCodes
            .Order(StringComparer.Ordinal)
            .ToArray();
        var completed = drainCompleted
            && failures.Length == 0
            && scheduling.StopReasonCode is null;
        var cpuPercent = elapsed <= TimeSpan.Zero
            ? 0
            : Math.Max(
                0,
                (after.CpuTime - before.CpuTime).TotalMilliseconds
                / elapsed.TotalMilliseconds
                / Environment.ProcessorCount
                * 100d);
        return new KafkaCapacitySampleEvidence(
            KafkaCapacityScopeCodes.KafkaTransport,
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
                scheduling.Scheduled / denominator,
                integrity.Acknowledged / denominator,
                integrity.Consumed / denominator,
                state.ScheduleLatency.Snapshot(),
                state.AcknowledgementLatency.Snapshot(),
                state.EndToEndLatency.Snapshot(),
                drainMilliseconds,
                cpuPercent,
                after.ManagedHeapBytes,
                statistics.Select(static item => item.MessageCount)
                    .DefaultIfEmpty()
                    .Max(),
                Math.Max(0, after.AllocatedBytes - before.AllocatedBytes),
                after.WorkingSetBytes,
                Math.Max(0, after.Gen0Collections - before.Gen0Collections),
                Math.Max(0, after.Gen1Collections - before.Gen1Collections),
                Math.Max(0, after.Gen2Collections - before.Gen2Collections)),
            failures);
    }

    private void OnStatistics(string json)
    {
        try
        {
            statistics.Enqueue(
                KafkaCapacityLibrdkafkaStatisticsProjection.Parse(json));
            while (statistics.Count > 3_600)
            {
                statistics.TryDequeue(out _);
            }
        }
        catch (JsonException)
        {
            // Statistics 不是可靠性数据；解析失败由缺少资源证据反映，不传播原文。
        }
    }

    private static KafkaCapacitySchedulingResult EmptySchedulingResult(
        string reasonCode) =>
        new(0, 0, 0, 0, reasonCode);

    private sealed class PhaseState(
        KafkaCapacitySampleContext context,
        uint sampleHash)
    {
        private readonly ConcurrentDictionary<string, byte> failures =
            new(StringComparer.Ordinal);

        public uint SampleHash { get; } = sampleHash;

        public KafkaCapacityIntegrityTracker Tracker { get; } = new(
            context.MaximumMessages,
            context.TopicIdentity.Partitions);

        public KafkaCapacityLatencyHistogram ScheduleLatency { get; } = new();

        public KafkaCapacityLatencyHistogram AcknowledgementLatency { get; } = new();

        public KafkaCapacityLatencyHistogram EndToEndLatency { get; } = new();

        public IReadOnlyList<string> FailureCodes => failures.Keys.ToArray();

        public bool HasTerminalDeliveryFailure =>
            failures.ContainsKey("delivery_not_persisted")
            || failures.ContainsKey("produce_failed")
            || failures.ContainsKey("payload_corrupted")
            || failures.ContainsKey("consume_tracking_failed")
            || failures.ContainsKey("delivery_tracking_failed");

        public void AddFailure(string failureCode) =>
            failures.TryAdd(failureCode, 0);

        public void OnDelivery(
            KafkaCapacityDeliveryReport report,
            long enqueuedTimestamp,
            long acknowledgedTimestamp,
            CancellationTokenSource phaseCancellation)
        {
            if (!report.Persisted)
            {
                AddFailure("delivery_not_persisted");
                phaseCancellation.Cancel();
                return;
            }

            try
            {
                Tracker.OnAcknowledged(report.GlobalSequence);
                AcknowledgementLatency.RecordMicroseconds(Math.Max(
                    1,
                    acknowledgedTimestamp - enqueuedTimestamp));
            }
            catch
            {
                AddFailure("delivery_tracking_failed");
                phaseCancellation.Cancel();
            }
        }

        public void OnConsumed(
            KafkaCapacityConsumedMessage message,
            long consumedTimestamp)
        {
            if (!KafkaCapacityEnvelopeCodec.TryDecode(
                    message.Value,
                    out var envelope)
                || envelope.RunHash != context.RunHash
                || envelope.SampleHash != SampleHash)
            {
                Tracker.OnCorrupted();
                AddFailure("payload_corrupted");
                return;
            }

            try
            {
                Tracker.OnConsumed(
                    envelope.GlobalSequence,
                    message.Partition,
                    envelope.PartitionSequence,
                    payloadValid: true);
                EndToEndLatency.RecordMicroseconds(Math.Max(
                    1,
                    consumedTimestamp - envelope.ScheduledTimestamp));
            }
            catch
            {
                AddFailure("consume_tracking_failed");
            }
        }
    }

    private sealed record PhaseResult(
        PhaseState State,
        KafkaCapacitySchedulingResult Scheduling,
        KafkaCapacityIntegrityEvidence Evidence,
        IReadOnlyList<string> FailureCodes,
        long DrainMilliseconds,
        bool DrainCompleted);

    private readonly record struct ResourceSnapshot(
        TimeSpan CpuTime,
        long AllocatedBytes,
        long ManagedHeapBytes,
        long WorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections)
    {
        public static ResourceSnapshot Capture()
        {
            using var process = Process.GetCurrentProcess();
            return new ResourceSnapshot(
                process.TotalProcessorTime,
                GC.GetTotalAllocatedBytes(precise: false),
                GC.GetTotalMemory(forceFullCollection: false),
                process.WorkingSet64,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }
}

public interface IKafkaCapacityStatisticsSource
{
    IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> SnapshotStatistics();
}

internal sealed class SystemKafkaCapacityClock : IKafkaCapacityClock
{
    public static readonly SystemKafkaCapacityClock Instance = new();

    public long GetTimestampMicroseconds() =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10;

    public async ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        await Task.Delay(delay, TimeProvider.System, cancellationToken);
}
