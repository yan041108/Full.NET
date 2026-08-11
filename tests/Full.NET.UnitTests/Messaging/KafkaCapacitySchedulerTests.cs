using Full.NET.Benchmarks.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacitySchedulerTests
{
    [TestMethod]
    public async Task Open_loop_deadlines_do_not_depend_on_previous_completion()
    {
        var clock = new ManualClock();
        var scheduler = new KafkaCapacityOpenLoopScheduler(clock);
        var messages = new List<KafkaCapacityScheduledMessage>();

        var result = await scheduler.RunAsync(
            targetMessagesPerSecond: 10,
            duration: TimeSpan.FromSeconds(2),
            maximumMessages: 100,
            producerConcurrency: 1,
            (message, _) =>
            {
                lock (messages)
                {
                    messages.Add(message);
                }

                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(20L, result.Scheduled);
        Assert.AreEqual(0L, result.Missed);
        Assert.IsNull(result.StopReasonCode);
        var ordered = messages.OrderBy(static message => message.GlobalSequence).ToArray();
        Assert.HasCount(20, ordered);
        Assert.AreEqual(0L, ordered[0].ScheduledTimestampMicroseconds);
        Assert.AreEqual(1_900_000L, ordered[^1].ScheduledTimestampMicroseconds);
    }

    [TestMethod]
    public async Task Scheduler_uses_bounded_channel_when_sink_is_blocked()
    {
        var clock = new ManualClock();
        var scheduler = new KafkaCapacityOpenLoopScheduler(clock);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 1;

        var running = scheduler.RunAsync(
            targetMessagesPerSecond: 1_000_000,
            duration: TimeSpan.FromSeconds(1),
            maximumMessages: 5_000,
            producerConcurrency: 1,
            async (_, cancellationToken) =>
            {
                if (Interlocked.Exchange(ref first, 0) == 1)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(cancellationToken);
                }
            },
            CancellationToken.None);

        await entered.Task;
        await Task.Delay(50);
        Assert.IsFalse(running.IsCompleted);
        release.SetResult();
        var result = await running;

        Assert.AreEqual(5_000L, result.Scheduled);
        Assert.IsLessThanOrEqualTo(4_096, result.MaximumBufferedMessages);
        Assert.AreEqual(4_096, result.ChannelCapacity);
    }

    [TestMethod]
    public async Task Scheduler_stops_after_ten_under_target_seconds_without_burst_catchup()
    {
        var clock = new ManualClock(delayMultiplier: 20);
        var scheduler = new KafkaCapacityOpenLoopScheduler(clock);

        var result = await scheduler.RunAsync(
            10,
            TimeSpan.FromSeconds(30),
            1_000,
            1,
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None);

        Assert.AreEqual("scheduling_rate_below_95_percent", result.StopReasonCode);
        Assert.IsGreaterThan(0L, result.Missed);
        Assert.IsLessThan(20L, result.Scheduled);
    }

    [TestMethod]
    public async Task Scheduler_does_not_send_a_catchup_message_after_sample_end()
    {
        var clock = new ManualClock(delayMultiplier: 1_000);
        var scheduler = new KafkaCapacityOpenLoopScheduler(clock);

        var result = await scheduler.RunAsync(
            10,
            TimeSpan.FromSeconds(1),
            100,
            1,
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None);

        Assert.AreEqual(1L, result.Scheduled);
        Assert.IsGreaterThan(0L, result.Missed);
    }

    [TestMethod]
    public async Task Cancellation_stops_new_scheduling()
    {
        var clock = new ManualClock(blockDelays: true);
        var scheduler = new KafkaCapacityOpenLoopScheduler(clock);
        using var cancellation = new CancellationTokenSource();
        var scheduled = 0;
        var running = scheduler.RunAsync(
            10,
            TimeSpan.FromSeconds(10),
            100,
            1,
            (_, _) =>
            {
                Interlocked.Increment(ref scheduled);
                return ValueTask.CompletedTask;
            },
            cancellation.Token);

        await clock.DelayEntered.Task;
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await running);
        var afterCancellation = Volatile.Read(ref scheduled);
        await Task.Delay(20);
        Assert.AreEqual(afterCancellation, Volatile.Read(ref scheduled));
    }

    [TestMethod]
    public async Task Transport_driver_owns_scope_and_context_generates_only_temporary_group()
    {
        var sample = KafkaCapacityScenarioCatalog.Build(
            KafkaCapacityOptions.Parse([]))[0];
        var topic = new KafkaCapacityTopicIdentity(
            "cluster",
            "fullnet.capacity.topic",
            "topic-id",
            2,
            1);
        var context = KafkaCapacitySampleContext.Create(
            sample,
            topic,
            "run-a");
        var driver = new KafkaTransportScenarioDriver(
            new RecordingTransportExecutor());

        var evidence = await driver.ExecuteAsync(context, CancellationToken.None);

        Assert.AreEqual(KafkaCapacityScopeCodes.KafkaTransport, driver.ScopeCode);
        Assert.StartsWith("fullnet.capacity.", context.ConsumerGroupId);
        Assert.AreEqual(topic.TopicName, context.TopicIdentity.TopicName);
        Assert.AreEqual(sample.SampleId, evidence.SampleId);
    }

    [TestMethod]
    public async Task Transport_driver_rejects_evidence_for_another_sample()
    {
        var sample = KafkaCapacityScenarioCatalog.Build(
            KafkaCapacityOptions.Parse([]))[0];
        var context = KafkaCapacitySampleContext.Create(
            sample,
            new KafkaCapacityTopicIdentity("cluster", "topic", "id", 1, 1),
            "run");
        var driver = new KafkaTransportScenarioDriver(
            new RecordingTransportExecutor(sampleIdOverride: "other"));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            driver.ExecuteAsync(context, CancellationToken.None));
    }

    [TestMethod]
    public async Task Checkpoint_rejects_evidence_from_a_different_driver_scope()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fullnet-scope-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "checkpoint.json");
        var topic = new KafkaCapacityTopicIdentity("cluster", "topic", "id", 1, 1);
        var checkpoint = KafkaCapacityCheckpoint.Create(
            "build",
            "scenario",
            KafkaCapacityScopeCodes.KafkaTransport,
            topic);
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                KafkaCapacityCheckpoint.SaveCompletedAsync(
                    path,
                    checkpoint,
                    "sample",
                    sampleCompleted: true,
                    scopeCode: "future_worker_scope",
                    cancellationToken: CancellationToken.None));
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RecordingTransportExecutor(
        string? sampleIdOverride = null) : IKafkaCapacityTransportExecutor
    {
        public Task<KafkaCapacitySampleEvidence> ExecuteAsync(
            KafkaCapacitySampleContext context,
            CancellationToken cancellationToken)
        {
            var latency = new KafkaCapacityLatencySnapshot(0, 0, 0, 0, 0, 0, 0);
            return Task.FromResult(new KafkaCapacitySampleEvidence(
                KafkaCapacityScopeCodes.KafkaTransport,
                sampleIdOverride ?? context.Sample.SampleId,
                context.Sample.Scenario,
                context.Sample.TargetMessagesPerSecond,
                context.Sample.PayloadSizeBytes,
                context.TopicIdentity.Partitions,
                context.Sample.ProducerConcurrency,
                KafkaCapacitySampleState.Completed,
                new KafkaCapacityIntegrityEvidence(0, 0, 0, 0, 0, 0, 0, 0, 0, true),
                new KafkaCapacityPerformanceEvidence(0, 0, 0, latency, latency, latency, 0, 0, 0, 0),
                []));
        }
    }

    private sealed class ManualClock(
        int delayMultiplier = 1,
        bool blockDelays = false) : IKafkaCapacityClock
    {
        private long timestampMicroseconds;

        public TaskCompletionSource DelayEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public long GetTimestampMicroseconds() =>
            Volatile.Read(ref timestampMicroseconds);

        public async ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            DelayEntered.TrySetResult();
            if (blockDelays)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return;
            }

            var microseconds = (long)Math.Ceiling(
                delay.TotalMilliseconds * 1_000d);
            Interlocked.Add(
                ref timestampMicroseconds,
                checked(microseconds * delayMultiplier));
        }
    }
}
