using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 运行独立 Producer/Consumer 传输样本并形成固定内存正确性与性能证据。
/// </summary>
public sealed class KafkaCapacityTransportExecutor : IKafkaCapacityTransportExecutor,
    IKafkaCapacityStatisticsSource
{
    private const uint WarmupHashMask = 0xA5A5_A5A5;
    private const long DefaultMaximumManagedHeapBytes = 2L * 1024 * 1024 * 1024;
    private const long DefaultMaximumScheduleLatencyMicroseconds = 5_000_000;
    private readonly KafkaMessagingOptions options;
    private readonly IKafkaCapacityProducerFactory producerFactory;
    private readonly IKafkaCapacityConsumerFactory consumerFactory;
    private readonly IKafkaCapacityWorkloadScheduler scheduler;
    private readonly IKafkaCapacityClock clock;
    private readonly long maximumManagedHeapBytes;
    private readonly long maximumScheduleLatencyMicroseconds;
    private readonly ConcurrentDictionary<string, SampleStatisticsBuffer>
        statisticsBySample = new(StringComparer.Ordinal);
    private string statisticsSampleId = "unassigned";
    private string statisticsPhase = "initialization";

    public KafkaCapacityTransportExecutor(
        KafkaMessagingOptions options,
        IKafkaCapacityProducerFactory producerFactory,
        IKafkaCapacityConsumerFactory consumerFactory)
        : this(
            options,
            producerFactory,
            consumerFactory,
            new KafkaCapacityOpenLoopScheduler(),
            SystemKafkaCapacityClock.Instance,
            DefaultMaximumManagedHeapBytes,
            DefaultMaximumScheduleLatencyMicroseconds)
    {
    }

    internal KafkaCapacityTransportExecutor(
        KafkaMessagingOptions options,
        IKafkaCapacityProducerFactory producerFactory,
        IKafkaCapacityConsumerFactory consumerFactory,
        IKafkaCapacityWorkloadScheduler scheduler,
        IKafkaCapacityClock clock,
        long maximumManagedHeapBytes = DefaultMaximumManagedHeapBytes,
        long maximumScheduleLatencyMicroseconds =
            DefaultMaximumScheduleLatencyMicroseconds)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.producerFactory = producerFactory
            ?? throw new ArgumentNullException(nameof(producerFactory));
        this.consumerFactory = consumerFactory
            ?? throw new ArgumentNullException(nameof(consumerFactory));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumManagedHeapBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumScheduleLatencyMicroseconds);
        this.maximumManagedHeapBytes = maximumManagedHeapBytes;
        this.maximumScheduleLatencyMicroseconds =
            maximumScheduleLatencyMicroseconds;
    }

    public IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> SnapshotStatistics() =>
        statisticsBySample.Values
            .SelectMany(static sample => sample.Snapshot())
            .OrderBy(static item => item.SampleId, StringComparer.Ordinal)
            .ThenBy(static item => item.Phase, StringComparer.Ordinal)
            .ToArray();

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Volatile.Write(ref statisticsSampleId, context.Sample.SampleId);
        Volatile.Write(ref statisticsPhase, "initialization");
        var statisticsHandler = new Action<string>(OnStatistics);
        await using var producer = producerFactory.Create(
            options,
            context.ProducerClientId,
            statisticsHandler);
        await using var consumer = consumerFactory.Create(
            options,
            context.ConsumerGroupId,
            context.ConsumerClientId,
            context.TopicIdentity.Partitions,
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
        long brokerOffsetBacklogAtStop = 0;
        long oldestUnconsumedAgeUpperBoundMicroseconds = 0;
        long brokerOffsetBacklogAtDrainCompletion = 0;
        long oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds = 0;
        long drainedMessages = 0;
        var drainCompleted = false;
        var consumerStopBudget = context.DrainTimeout;
        var warmupFailed = false;
        try
        {
            if (context.Warmup > TimeSpan.Zero)
            {
                Volatile.Write(ref statisticsPhase, "warmup");
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
                    consumer,
                    cancellationToken);
                if (!warmupResult.Evidence.CorrectnessPassed
                    || warmupResult.FailureCodes.Count > 0)
                {
                    var failedState = new PhaseState(context, context.SampleHash);
                    failedState.AddFailure("warmup_failed");
                    finalState = failedState;
                    finalScheduling = EmptySchedulingResult("warmup_failed");
                    drainMilliseconds = warmupResult.DrainMilliseconds;
                    consumerStopBudget = warmupResult.RemainingDrainBudget;
                    warmupFailed = true;
                }
            }

            if (!warmupFailed)
            {
                Volatile.Write(ref statisticsPhase, "measurement");
                Volatile.Write(ref activePhase, finalState);
                before = ResourceSnapshot.Capture();
                var executionStopwatch = Stopwatch.StartNew();
                var productionResult = await RunPhaseAsync(
                    context,
                    context.Duration,
                    context.MaximumMessages,
                    finalState,
                    producer,
                    consumer,
                    cancellationToken);
                executionStopwatch.Stop();
                after = ResourceSnapshot.Capture();
                elapsed = executionStopwatch.Elapsed;
                finalScheduling = productionResult.Scheduling;
                drainMilliseconds = productionResult.DrainMilliseconds;
                drainCompleted = productionResult.DrainCompleted;
                consumerStopBudget = productionResult.RemainingDrainBudget;
                brokerOffsetBacklogAtStop =
                    productionResult.BrokerOffsetBacklogAtStop;
                oldestUnconsumedAgeUpperBoundMicroseconds =
                    productionResult.OldestUnconsumedAgeUpperBoundMicroseconds;
                drainedMessages = productionResult.DrainedMessages;
                brokerOffsetBacklogAtDrainCompletion =
                    productionResult.BrokerOffsetBacklogAtDrainCompletion;
                oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds =
                    productionResult
                        .OldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds;
            }
        }
        finally
        {
            Volatile.Write(ref activePhase, null);
            using var stopTimeout = new CancellationTokenSource();
            if (consumerStopBudget > TimeSpan.Zero)
            {
                stopTimeout.CancelAfter(consumerStopBudget);
            }
            else
            {
                stopTimeout.Cancel();
            }
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
            drainCompleted,
            brokerOffsetBacklogAtStop,
            oldestUnconsumedAgeUpperBoundMicroseconds,
            drainedMessages,
            brokerOffsetBacklogAtDrainCompletion,
            oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds);
    }

    private async Task<PhaseResult> RunPhaseAsync(
        KafkaCapacitySampleContext context,
        TimeSpan duration,
        int maximumMessages,
        PhaseState state,
        IKafkaCapacityProducer producer,
        IKafkaCapacityConsumer consumer,
        CancellationToken cancellationToken)
    {
        using var phaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        phaseCancellation.CancelAfter(duration + context.DrainTimeout);
        state.AttachCancellation(phaseCancellation);
        var phaseStarted = Stopwatch.GetTimestamp();
        var resourceMonitor = MonitorResourcesAsync(
            state,
            Math.Min(
                maximumScheduleLatencyMicroseconds,
                context.MaximumScheduleLatencyMicroseconds),
            phaseCancellation.Token);
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
        long scheduledToLanes = 0;

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
                    Interlocked.Increment(ref scheduledToLanes);
                },
                phaseCancellation.Token);
            var completed = await Task.WhenAny(schedulingTask, consumer.Completion);
            if (completed == consumer.Completion)
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

        var drainDeadline = new DrainDeadline(context.DrainTimeout);
        Volatile.Write(ref statisticsPhase, "drain");
        var laneDrainBudget = drainDeadline.Remaining;
        if (laneDrainBudget > TimeSpan.Zero)
        {
            phaseCancellation.CancelAfter(laneDrainBudget);
            try
            {
                await Task.WhenAll(laneTasks).WaitAsync(laneDrainBudget);
            }
            catch (TimeoutException)
            {
                state.AddFailure("producer_lane_drain_timeout");
                phaseCancellation.Cancel();
            }
            catch (OperationCanceledException)
            {
                // 取消后丢弃尚未进入 Producer 的 Lane 项，已入队项仍由 Flush 排空。
            }
            catch
            {
                state.AddFailure("producer_lane_failed");
            }
        }
        else
        {
            state.AddFailure("producer_lane_drain_timeout");
            phaseCancellation.Cancel();
        }

        if (scheduling.StopReasonCode is not null)
        {
            state.AddFailure(scheduling.StopReasonCode);
        }

        state.ApplyScheduleLatencyGate(
            Math.Min(
                maximumScheduleLatencyMicroseconds,
                context.MaximumScheduleLatencyMicroseconds),
            minimumCount: 1);

        if (scheduling.Scheduled == 0 && scheduledToLanes > 0)
        {
            scheduling = scheduling with
            {
                Scheduled = scheduledToLanes,
                ActiveDurationMicroseconds = Math.Max(
                    1,
                    (long)Stopwatch.GetElapsedTime(phaseStarted).TotalMicroseconds),
            };
        }

        var drainStopwatch = Stopwatch.StartNew();
        var beforeDrain = state.Tracker.Complete(drainCompleted: false);
        var backlogAtStop = await CaptureBacklogAsync(
            consumer,
            drainDeadline,
            state,
            cancellationToken);
        var brokerOffsetBacklogAtStop = backlogAtStop?.MessageCount ?? 0;
        var oldestUnconsumedAgeUpperBoundMicroseconds =
            brokerOffsetBacklogAtStop == 0 || state.FirstEnqueuedTimestamp < 0
                ? 0
                : Math.Max(
                    0,
                    clock.GetTimestampMicroseconds()
                    - state.FirstEnqueuedTimestamp);
        int flushRemaining;
        try
        {
            flushRemaining = producer.Flush(drainDeadline.Remaining);
        }
        catch
        {
            state.AddFailure("producer_flush_failed");
            phaseCancellation.Cancel();
            flushRemaining = int.MaxValue;
        }
        if (flushRemaining != 0)
        {
            state.AddFailure("producer_flush_incomplete");
        }

        var drainCompleted = false;
        while (drainDeadline.Remaining > TimeSpan.Zero)
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

        var backlogAtDrainCompletion = await CaptureBacklogAsync(
            consumer,
            drainDeadline,
            state,
            cancellationToken);
        var brokerOffsetBacklogAtDrainCompletion =
            backlogAtDrainCompletion?.MessageCount ?? 0;
        if (backlogAtDrainCompletion is null
            || brokerOffsetBacklogAtDrainCompletion != 0)
        {
            drainCompleted = false;
        }

        var oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds =
            brokerOffsetBacklogAtDrainCompletion == 0
            || state.FirstEnqueuedTimestamp < 0
                ? 0
                : Math.Max(
                    0,
                    clock.GetTimestampMicroseconds()
                    - state.FirstEnqueuedTimestamp);
        drainStopwatch.Stop();
        phaseCancellation.Cancel();
        await resourceMonitor;
        var finalIntegrity = state.Tracker.Complete(drainCompleted);
        return new PhaseResult(
            state,
            scheduling,
            finalIntegrity,
            state.FailureCodes,
            drainStopwatch.ElapsedMilliseconds,
            drainCompleted,
            drainDeadline.Remaining,
            brokerOffsetBacklogAtStop,
            oldestUnconsumedAgeUpperBoundMicroseconds,
            Math.Max(0, finalIntegrity.Consumed - beforeDrain.Consumed),
            brokerOffsetBacklogAtDrainCompletion,
            oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds);
    }

    private static async Task<KafkaCapacityBrokerBacklogSnapshot?> CaptureBacklogAsync(
        IKafkaCapacityConsumer consumer,
        DrainDeadline deadline,
        PhaseState state,
        CancellationToken cancellationToken)
    {
        var remaining = deadline.Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            state.AddFailure("broker_backlog_capture_failed");
            return null;
        }

        try
        {
            return await consumer.CaptureBacklogAsync(
                remaining,
                cancellationToken);
        }
        catch
        {
            state.AddFailure("broker_backlog_capture_failed");
            return null;
        }
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
            while (true)
            {
                phaseCancellation.Token.ThrowIfCancellationRequested();
                var enqueuedTimestamp = clock.GetTimestampMicroseconds();
                var scheduleLatency = Math.Max(
                    1,
                    enqueuedTimestamp - scheduled.ScheduledTimestampMicroseconds);
                var value = KafkaCapacityEnvelopeCodec.Encode(
                    context.Sample.PayloadSizeBytes,
                    context.RunHash,
                    state.SampleHash,
                    scheduled.GlobalSequence,
                    partitionSequence,
                    scheduled.ScheduledTimestampMicroseconds,
                    enqueuedTimestamp);
                state.Tracker.OnEnqueued(scheduled.GlobalSequence);
                try
                {
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
                    if (!state.ScheduleLatency.RecordMicroseconds(scheduleLatency))
                    {
                        state.CancelForFailure("latency_histogram_overflow");
                    }
                    state.ObserveEnqueuedTimestamp(enqueuedTimestamp);
                    break;
                }
                catch (KafkaException exception)
                    when (exception.Error.Code == ErrorCode.Local_QueueFull)
                {
                    state.Tracker.OnEnqueueRejected(scheduled.GlobalSequence);
                    await clock.DelayAsync(
                        TimeSpan.FromMilliseconds(1),
                        phaseCancellation.Token);
                }
                catch
                {
                    state.AddFailure("produce_failed");
                    phaseCancellation.Cancel();
                    throw;
                }
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
        bool drainCompleted,
        long brokerOffsetBacklogAtStop,
        long oldestUnconsumedAgeUpperBoundMicroseconds,
        long drainedMessages,
        long brokerOffsetBacklogAtDrainCompletion,
        long oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds)
    {
        var integrity = state.Tracker.Complete(drainCompleted);
        var scheduleLatency = state.ScheduleLatency.Snapshot();
        var acknowledgementLatency = state.AcknowledgementLatency.Snapshot();
        var endToEndLatency = state.EndToEndLatency.Snapshot();
        if (!scheduleLatency.IsValid
            || !acknowledgementLatency.IsValid
            || !endToEndLatency.IsValid)
        {
            state.AddFailure("latency_histogram_overflow");
        }

        var denominator = Math.Max(
            0.001d,
            scheduling.ActiveDurationMicroseconds / 1_000_000d);
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
        statisticsBySample.TryGetValue(
            context.Sample.SampleId,
            out var sampleStatistics);
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
                scheduleLatency,
                acknowledgementLatency,
                endToEndLatency,
                drainMilliseconds,
                cpuPercent,
                Math.Max(after.ManagedHeapBytes, state.MaximumManagedHeapBytes),
                sampleStatistics?.MaximumMessageCount ?? 0,
                Math.Max(0, after.AllocatedBytes - before.AllocatedBytes),
                after.WorkingSetBytes,
                Math.Max(0, after.Gen0Collections - before.Gen0Collections),
                Math.Max(0, after.Gen1Collections - before.Gen1Collections),
                Math.Max(0, after.Gen2Collections - before.Gen2Collections),
                integrity.Enqueued / denominator,
                drainMilliseconds <= 0
                    ? 0
                    : drainedMessages / (drainMilliseconds / 1_000d),
                brokerOffsetBacklogAtStop,
                oldestUnconsumedAgeUpperBoundMicroseconds,
                Math.Max(after.ManagedHeapBytes, state.MaximumManagedHeapBytes),
                Math.Max(
                    Math.Max(before.WorkingSetBytes, after.WorkingSetBytes),
                    state.MaximumWorkingSetBytes),
                brokerOffsetBacklogAtDrainCompletion,
                oldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds,
                sampleStatistics?.DroppedSnapshots ?? 0),
            failures);
    }

    private async Task MonitorResourcesAsync(
        PhaseState state,
        long maximumAllowedScheduleP99Microseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var process = Process.GetCurrentProcess();
                state.ObserveResources(
                    GC.GetTotalMemory(forceFullCollection: false),
                    process.WorkingSet64,
                    maximumManagedHeapBytes);
                state.ApplyScheduleLatencyGate(
                    maximumAllowedScheduleP99Microseconds,
                    minimumCount: 100);
                await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // 资源采样与阶段共享停止令牌；取消只结束采样，不覆盖主故障码。
        }
    }

    private void OnStatistics(string json)
    {
        try
        {
            var sampleId = Volatile.Read(ref statisticsSampleId);
            var evidence = KafkaCapacityLibrdkafkaStatisticsProjection.Parse(
                json,
                sampleId,
                Volatile.Read(ref statisticsPhase));
            statisticsBySample.GetOrAdd(
                    sampleId,
                    static _ => new SampleStatisticsBuffer())
                .Add(evidence);
        }
        catch (JsonException)
        {
            // Statistics 不是可靠性数据；解析失败由缺少资源证据反映，不传播原文。
        }
    }

    private static KafkaCapacitySchedulingResult EmptySchedulingResult(
        string reasonCode) =>
        new(0, 0, 0, 0, 0, reasonCode);

    private sealed class PhaseState(
        KafkaCapacitySampleContext context,
        uint sampleHash)
    {
        private readonly ConcurrentDictionary<string, byte> failures =
            new(StringComparer.Ordinal);
        private CancellationTokenSource? phaseCancellation;
        private long maximumManagedHeapBytes;
        private long maximumWorkingSetBytes;
        private long firstEnqueuedTimestamp = long.MaxValue;

        public uint SampleHash { get; } = sampleHash;

        public KafkaCapacityIntegrityTracker Tracker { get; } = new(
            context.MaximumMessages,
            context.TopicIdentity.Partitions);

        public KafkaCapacityLatencyHistogram ScheduleLatency { get; } = new();

        public KafkaCapacityLatencyHistogram AcknowledgementLatency { get; } = new();

        public KafkaCapacityLatencyHistogram EndToEndLatency { get; } = new();

        public IReadOnlyList<string> FailureCodes => failures.Keys.ToArray();

        public long MaximumManagedHeapBytes =>
            Volatile.Read(ref maximumManagedHeapBytes);

        public long MaximumWorkingSetBytes =>
            Volatile.Read(ref maximumWorkingSetBytes);

        public long FirstEnqueuedTimestamp
        {
            get
            {
                var value = Volatile.Read(ref firstEnqueuedTimestamp);
                return value == long.MaxValue ? -1 : value;
            }
        }

        public bool HasTerminalDeliveryFailure =>
            failures.ContainsKey("delivery_not_persisted")
            || failures.ContainsKey("produce_failed")
            || failures.ContainsKey("producer_flush_failed")
            || failures.ContainsKey("payload_corrupted")
            || failures.ContainsKey("consume_integrity_failed")
            || failures.ContainsKey("consume_tracking_failed")
            || failures.ContainsKey("delivery_tracking_failed");

        public void AddFailure(string failureCode) =>
            failures.TryAdd(failureCode, 0);

        public void AttachCancellation(
            CancellationTokenSource cancellation) =>
            Volatile.Write(ref phaseCancellation, cancellation);

        public void CancelForFailure(string failureCode)
        {
            AddFailure(failureCode);
            Volatile.Read(ref phaseCancellation)?.Cancel();
        }

        public void ObserveResources(
            long managedHeapBytes,
            long workingSetBytes,
            long maximumAllowedManagedHeapBytes)
        {
            UpdateMaximum(ref maximumManagedHeapBytes, managedHeapBytes);
            UpdateMaximum(ref maximumWorkingSetBytes, workingSetBytes);
            if (managedHeapBytes > maximumAllowedManagedHeapBytes)
            {
                CancelForFailure("managed_heap_limit_exceeded");
            }
        }

        public void ApplyScheduleLatencyGate(
            long maximumAllowedP99Microseconds,
            long minimumCount)
        {
            var snapshot = ScheduleLatency.Snapshot();
            if (!snapshot.IsValid)
            {
                CancelForFailure("latency_histogram_overflow");
                return;
            }

            if (snapshot.Count >= minimumCount
                && snapshot.P99Microseconds > maximumAllowedP99Microseconds)
            {
                CancelForFailure("schedule_latency_limit_exceeded");
            }
        }

        public void ObserveEnqueuedTimestamp(long value)
        {
            var current = Volatile.Read(ref firstEnqueuedTimestamp);
            while (value < current)
            {
                var observed = Interlocked.CompareExchange(
                    ref firstEnqueuedTimestamp,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

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
                if (!AcknowledgementLatency.RecordMicroseconds(Math.Max(
                        1,
                        acknowledgedTimestamp - enqueuedTimestamp)))
                {
                    CancelForFailure("latency_histogram_overflow");
                }
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
                || envelope.RunHash != context.RunHash)
            {
                Tracker.OnCorrupted();
                CancelForFailure("payload_corrupted");
                return;
            }

            if (envelope.SampleHash != SampleHash)
            {
                // 同一 Run Topic 会保留先前样本；独立 Group 从 earliest 读取时必须安全跳过。
                return;
            }

            try
            {
                var integrityValid = Tracker.OnConsumed(
                    envelope.GlobalSequence,
                    message.Partition,
                    envelope.PartitionSequence,
                    payloadValid: true);
                if (!EndToEndLatency.RecordMicroseconds(Math.Max(
                        1,
                        consumedTimestamp - envelope.ScheduledTimestamp)))
                {
                    CancelForFailure("latency_histogram_overflow");
                }
                if (!integrityValid)
                {
                    CancelForFailure("consume_integrity_failed");
                }
            }
            catch
            {
                CancelForFailure("consume_tracking_failed");
            }
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref target,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed record PhaseResult(
        PhaseState State,
        KafkaCapacitySchedulingResult Scheduling,
        KafkaCapacityIntegrityEvidence Evidence,
        IReadOnlyList<string> FailureCodes,
        long DrainMilliseconds,
        bool DrainCompleted,
        TimeSpan RemainingDrainBudget,
        long BrokerOffsetBacklogAtStop,
        long OldestUnconsumedAgeUpperBoundMicroseconds,
        long DrainedMessages,
        long BrokerOffsetBacklogAtDrainCompletion,
        long OldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds);

    private sealed class SampleStatisticsBuffer
    {
        private const int Capacity = 512;
        private readonly ConcurrentQueue<KafkaCapacityLibrdkafkaStatisticsEvidence>
            snapshots = new();
        private long maximumMessageCount;
        private long droppedSnapshots;

        public long MaximumMessageCount => Volatile.Read(ref maximumMessageCount);

        public long DroppedSnapshots => Volatile.Read(ref droppedSnapshots);

        public void Add(KafkaCapacityLibrdkafkaStatisticsEvidence evidence)
        {
            if (evidence.Phase is "measurement" or "drain")
            {
                UpdateMaximum(ref maximumMessageCount, evidence.MessageCount);
            }
            snapshots.Enqueue(evidence);
            while (snapshots.Count > Capacity && snapshots.TryDequeue(out _))
            {
                Interlocked.Increment(ref droppedSnapshots);
            }
        }

        public IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> Snapshot() =>
            snapshots.ToArray();

        private static void UpdateMaximum(ref long target, long value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref target,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class DrainDeadline(TimeSpan budget)
    {
        private readonly long started = Stopwatch.GetTimestamp();

        public TimeSpan Remaining
        {
            get
            {
                var remaining = budget - Stopwatch.GetElapsedTime(started);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

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
