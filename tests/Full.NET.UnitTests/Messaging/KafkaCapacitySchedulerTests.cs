using Full.NET.Benchmarks.Kafka;
using Full.NET.Messaging.Kafka;

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

    [TestMethod]
    public async Task Transport_executor_assigns_consumer_before_warmup_and_excludes_warmup_evidence()
    {
        var events = new List<string>();
        var consumer = new RecordingCapacityConsumer(events);
        var producer = new RecordingCapacityProducer(events, consumer);
        var scheduler = new RecordingWorkloadScheduler(events);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(producer),
            new RecordingConsumerFactory(consumer),
            scheduler,
            new ManualClock());
        var context = CreateExecutionContext(warmup: TimeSpan.FromSeconds(1));

        var evidence = await executor.ExecuteAsync(
            context,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "consumer-start", "consumer-assigned" },
            events.Take(2).ToArray());
        Assert.AreEqual(2, producer.Produced);
        Assert.AreEqual(1L, evidence.Integrity.Enqueued);
        Assert.AreEqual(1L, evidence.Integrity.Acknowledged);
        Assert.AreEqual(1L, evidence.Integrity.Consumed);
        Assert.IsTrue(evidence.Integrity.CorrectnessPassed);
        var finalFlush = events.LastIndexOf("producer-flush");
        Assert.IsTrue(events.IndexOf("schedule-end-2") < finalFlush);
        Assert.IsTrue(finalFlush < events.IndexOf("consumer-stop"));
    }

    [TestMethod]
    public async Task Transport_executor_fails_correctness_for_non_persisted_delivery_and_corruption()
    {
        var deliveryEvents = new List<string>();
        var deliveryConsumer = new RecordingCapacityConsumer(deliveryEvents);
        var deliveryExecutor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(
                deliveryEvents,
                deliveryConsumer,
                persisted: false)),
            new RecordingConsumerFactory(deliveryConsumer),
            new RecordingWorkloadScheduler(deliveryEvents),
            new ManualClock());
        var deliveryEvidence = await deliveryExecutor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(deliveryEvidence.Integrity.CorrectnessPassed);
        CollectionAssert.Contains(
            deliveryEvidence.FailureCodes.ToArray(),
            "delivery_not_persisted");

        var corruptionEvents = new List<string>();
        var corruptionConsumer = new RecordingCapacityConsumer(
            corruptionEvents,
            corruptPayload: true);
        var corruptionExecutor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(
                corruptionEvents,
                corruptionConsumer)),
            new RecordingConsumerFactory(corruptionConsumer),
            new RecordingWorkloadScheduler(corruptionEvents),
            new ManualClock());
        var corruptionEvidence = await corruptionExecutor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(1L, corruptionEvidence.Integrity.Corrupted);
        Assert.IsFalse(corruptionEvidence.Integrity.CorrectnessPassed);
    }

    [TestMethod]
    public async Task Transport_executor_cancellation_stops_sending_then_flushes_and_closes()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var consumer = new RecordingCapacityConsumer(events);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(events, consumer)),
            new RecordingConsumerFactory(consumer),
            new CancellingWorkloadScheduler(events, cancellation),
            new ManualClock());

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            cancellation.Token);

        Assert.AreEqual(KafkaCapacitySampleState.Incomplete, evidence.State);
        CollectionAssert.Contains(evidence.FailureCodes.ToArray(), "cancelled");
        var flush = events.LastIndexOf("producer-flush");
        Assert.IsTrue(events.IndexOf("schedule-cancel") < flush);
        Assert.IsTrue(flush < events.IndexOf("consumer-stop"));
    }

    [TestMethod]
    public async Task Sample_runner_stops_when_checkpoint_persistence_fails()
    {
        var driver = new CountingScenarioDriver();
        var checkpointStore = new ThrowingCheckpointStore();
        var runner = new KafkaCapacityRunner(driver, checkpointStore);
        var context = CreateExecutionContext(TimeSpan.Zero);

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            runner.ExecuteSamplesAsync(
                [context, context],
                "checkpoint.json",
                KafkaCapacityCheckpoint.Create(
                    "build",
                    "scenario",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    context.TopicIdentity),
                CancellationToken.None));

        Assert.AreEqual(1, driver.Calls);
    }

    private static KafkaCapacitySampleContext CreateExecutionContext(TimeSpan warmup)
    {
        var sample = KafkaCapacityScenarioCatalog.Build(
            KafkaCapacityOptions.Parse([
                "--scenarios", "low-rate",
                "--duration-seconds", "1",
                "--max-messages-per-sample", "10",
            ]))[0];
        return KafkaCapacitySampleContext.Create(
            sample,
            new KafkaCapacityTopicIdentity("cluster", "topic", "id", 1, 1),
            "run",
            warmup,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            maximumMessages: 10);
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

    private sealed class RecordingProducerFactory(
        IKafkaCapacityProducer producer) : IKafkaCapacityProducerFactory
    {
        public IKafkaCapacityProducer Create(
            KafkaMessagingOptions options,
            Action<string> statisticsHandler) => producer;
    }

    private sealed class RecordingConsumerFactory(
        IKafkaCapacityConsumer consumer) : IKafkaCapacityConsumerFactory
    {
        public IKafkaCapacityConsumer Create(
            KafkaMessagingOptions options,
            string consumerGroupId,
            Action<string> statisticsHandler) => consumer;
    }

    private sealed class RecordingCapacityProducer(
        List<string> events,
        RecordingCapacityConsumer consumer,
        bool persisted = true) : IKafkaCapacityProducer
    {
        public int Produced { get; private set; }

        public void Produce(
            string topicName,
            int partition,
            string key,
            byte[] value,
            long globalSequence,
            Action<KafkaCapacityDeliveryReport> deliveryHandler)
        {
            Produced++;
            events.Add("produce");
            deliveryHandler(new KafkaCapacityDeliveryReport(
                globalSequence,
                persisted,
                persisted ? null : "not_persisted"));
            if (persisted)
            {
                consumer.EmitAsync(partition, value).GetAwaiter().GetResult();
            }
        }

        public int Flush(TimeSpan timeout)
        {
            events.Add("producer-flush");
            return 0;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingCapacityConsumer(
        List<string> events,
        bool corruptPayload = false) : IKafkaCapacityConsumer
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask>?
            messageHandler;

        public Task Completion => completion.Task;

        public Task StartAsync(
            string topicName,
            Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask> handler,
            CancellationToken cancellationToken)
        {
            events.Add("consumer-start");
            messageHandler = handler;
            return Task.CompletedTask;
        }

        public Task WaitForAssignmentAsync(CancellationToken cancellationToken)
        {
            events.Add("consumer-assigned");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("consumer-stop");
            completion.TrySetResult();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async ValueTask EmitAsync(int partition, byte[] value)
        {
            if (messageHandler is null)
            {
                throw new InvalidOperationException("Consumer has not started.");
            }

            var delivered = value;
            if (corruptPayload)
            {
                delivered = value.ToArray();
                delivered[0] ^= 0x7F;
            }

            await messageHandler(
                new KafkaCapacityConsumedMessage(partition, delivered),
                CancellationToken.None);
        }
    }

    private sealed class RecordingWorkloadScheduler(
        List<string> events) : IKafkaCapacityWorkloadScheduler
    {
        private int calls;

        public async Task<KafkaCapacitySchedulingResult> RunAsync(
            int targetMessagesPerSecond,
            TimeSpan duration,
            int maximumMessages,
            int producerConcurrency,
            Func<KafkaCapacityScheduledMessage, CancellationToken, ValueTask> writeAsync,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            events.Add($"schedule-start-{call}");
            await writeAsync(
                new KafkaCapacityScheduledMessage(0, 0),
                cancellationToken);
            events.Add($"schedule-end-{call}");
            return new KafkaCapacitySchedulingResult(1, 0, 1, 1, null);
        }
    }

    private sealed class CancellingWorkloadScheduler(
        List<string> events,
        CancellationTokenSource cancellation) : IKafkaCapacityWorkloadScheduler
    {
        public async Task<KafkaCapacitySchedulingResult> RunAsync(
            int targetMessagesPerSecond,
            TimeSpan duration,
            int maximumMessages,
            int producerConcurrency,
            Func<KafkaCapacityScheduledMessage, CancellationToken, ValueTask> writeAsync,
            CancellationToken cancellationToken)
        {
            await writeAsync(new KafkaCapacityScheduledMessage(0, 0), cancellationToken);
            events.Add("schedule-cancel");
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }

    private sealed class CountingScenarioDriver : IKafkaCapacityScenarioDriver
    {
        public string ScopeCode => KafkaCapacityScopeCodes.KafkaTransport;

        public int Calls { get; private set; }

        public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
            KafkaCapacitySampleContext context,
            CancellationToken cancellationToken)
        {
            Calls++;
            return await new RecordingTransportExecutor()
                .ExecuteAsync(context, cancellationToken);
        }
    }

    private sealed class ThrowingCheckpointStore : IKafkaCapacityCheckpointStore
    {
        public Task<KafkaCapacityCheckpoint> SaveAsync(
            string path,
            KafkaCapacityCheckpoint checkpoint,
            KafkaCapacitySampleEvidence evidence,
            CancellationToken cancellationToken) =>
            throw new IOException("checkpoint failed");
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
