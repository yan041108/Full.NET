using Full.NET.Benchmarks.Kafka;
using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacitySchedulerTests
{
    [TestMethod]
    public void Driver_registry_rejects_unknown_and_duplicate_scopes()
    {
        var transport = new RecordingDriverFactory(
            KafkaCapacityScopeCodes.KafkaTransport);
        var registry = new KafkaCapacityDriverRegistry([transport]);

        Assert.AreSame(transport, registry.GetRequired(
            KafkaCapacityScopeCodes.KafkaTransport));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            registry.GetRequired("worker_inbox_handler"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new KafkaCapacityDriverRegistry([
                transport,
                new RecordingDriverFactory(KafkaCapacityScopeCodes.KafkaTransport),
            ]));
    }

    [TestMethod]
    public void Driver_registry_rejects_runtime_with_a_different_scope()
    {
        var factory = new RecordingDriverFactory(
            KafkaCapacityScopeCodes.KafkaTransport,
            runtimeScopeCode: "worker_inbox_handler");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            KafkaCapacityDriverRegistry.CreateRuntime(
                factory,
                new KafkaCapacityConfiguration
                {
                    Kafka = new KafkaMessagingOptions
                    {
                        Enabled = true,
                        BootstrapServers = "broker",
                    },
                }));
    }
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
    public void Future_driver_scope_uses_an_isolated_client_identity_namespace()
    {
        var options = KafkaCapacityOptions.Parse([
            "--scope",
            "worker_inbox_handler",
        ]);
        var sample = KafkaCapacityScenarioCatalog.Build(options)[0];
        var context = KafkaCapacitySampleContext.Create(
            sample,
            new KafkaCapacityTopicIdentity("cluster", "topic", "id", 1, 1),
            "shared-run");

        Assert.EndsWith(
            ".worker_inbox_handler",
            context.ConsumerGroupId,
            StringComparison.Ordinal);
        Assert.Contains(
            ".worker_inbox_handler.",
            context.ProducerClientId,
            StringComparison.Ordinal);
        Assert.Contains(
            ".worker_inbox_handler.",
            context.ConsumerClientId,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Future_driver_scope_and_role_survive_client_identity_length_bound()
    {
        var options = KafkaCapacityOptions.Parse([
            "--scope",
            "worker_inbox_handler",
        ]);
        var sample = KafkaCapacityScenarioCatalog.Build(options)[0] with
        {
            SampleId = new string('s', 160),
        };
        var context = KafkaCapacitySampleContext.Create(
            sample,
            new KafkaCapacityTopicIdentity("cluster", "topic", "id", 1, 1),
            new string('r', 160));
        var otherContext = KafkaCapacitySampleContext.Create(
            sample with
            {
                SampleId = new string('s', 159) + "x",
            },
            new KafkaCapacityTopicIdentity("cluster", "topic", "id", 1, 1),
            new string('r', 160));

        Assert.IsLessThanOrEqualTo(200, context.ConsumerGroupId.Length);
        Assert.IsLessThanOrEqualTo(200, context.ProducerClientId.Length);
        Assert.IsLessThanOrEqualTo(200, context.ConsumerClientId.Length);
        Assert.EndsWith(
            ".worker_inbox_handler",
            context.ConsumerGroupId,
            StringComparison.Ordinal);
        Assert.EndsWith(
            ".worker_inbox_handler.producer",
            context.ProducerClientId,
            StringComparison.Ordinal);
        Assert.EndsWith(
            ".worker_inbox_handler.consumer",
            context.ConsumerClientId,
            StringComparison.Ordinal);
        Assert.AreNotEqual(context.ProducerClientId, context.ConsumerClientId);
        Assert.AreNotEqual(
            context.ConsumerGroupId,
            otherContext.ConsumerGroupId);
        Assert.AreNotEqual(
            context.ProducerClientId,
            otherContext.ProducerClientId);
        Assert.AreNotEqual(
            context.ConsumerClientId,
            otherContext.ConsumerClientId);
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
            topic,
            "run");
        var context = CreateExecutionContext(TimeSpan.Zero);
        var evidence = await new RecordingTransportExecutor()
            .ExecuteAsync(context, CancellationToken.None);
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                KafkaCapacityCheckpoint.SaveSampleAsync(
                    path,
                    checkpoint,
                    evidence with { ScopeCode = "future_worker_scope" },
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
        var producerFactory = new RecordingProducerFactory(producer);
        var consumerFactory = new RecordingConsumerFactory(consumer);
        var scheduler = new RecordingWorkloadScheduler(events);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            producerFactory,
            consumerFactory,
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
        Assert.AreEqual(4d, evidence.Performance.ScheduledMessagesPerSecond);
        Assert.AreEqual(4d, evidence.Performance.AcknowledgedMessagesPerSecond);
        Assert.AreEqual(4d, evidence.Performance.ConsumedMessagesPerSecond);
        Assert.AreEqual(4d, evidence.Performance.ProducerEnqueuedMessagesPerSecond);
        Assert.IsGreaterThan(0L, evidence.Performance.ManagedHeapPeakBytes);
        Assert.AreEqual(0L, evidence.Performance.BrokerOffsetBacklogAtDrainCompletion);
        Assert.AreEqual(
            0L,
            evidence.Performance
                .OldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds);
        var finalFlush = events.LastIndexOf("producer-flush");
        Assert.IsTrue(events.IndexOf("schedule-end-2") < finalFlush);
        Assert.IsTrue(finalFlush < events.IndexOf("consumer-stop"));
        StringAssert.Contains(producerFactory.ClientId!, ".producer");
        StringAssert.Contains(producerFactory.ClientId!, context.Sample.SampleId);
        StringAssert.Contains(consumerFactory.ClientId!, ".consumer");
        Assert.AreEqual(1, consumerFactory.ExpectedPartitions);
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
    public async Task Transport_executor_retries_local_queue_full_without_creating_false_loss()
    {
        var events = new List<string>();
        var consumer = new RecordingCapacityConsumer(events);
        var producer = new QueueFullThenAcceptingProducer(events, consumer);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(producer),
            new RecordingConsumerFactory(consumer),
            new RecordingWorkloadScheduler(events),
            new ManualClock());

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(2, producer.Attempts);
        Assert.AreEqual(1L, evidence.Integrity.Enqueued);
        Assert.AreEqual(1L, evidence.Integrity.Acknowledged);
        Assert.AreEqual(1L, evidence.Integrity.Consumed);
        Assert.IsTrue(evidence.Integrity.CorrectnessPassed);
        CollectionAssert.DoesNotContain(evidence.FailureCodes.ToArray(), "produce_failed");
    }

    [TestMethod]
    public async Task Transport_executor_rejects_latency_histogram_overflow()
    {
        var events = new List<string>();
        var consumer = new RecordingCapacityConsumer(events);
        var clock = new ManualClock();
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new OverflowingLatencyProducer(
                events,
                consumer,
                clock)),
            new RecordingConsumerFactory(consumer),
            new RecordingWorkloadScheduler(events),
            clock);

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(KafkaCapacitySampleState.Incomplete, evidence.State);
        Assert.IsFalse(evidence.Performance.AcknowledgementLatency.IsValid);
        CollectionAssert.Contains(
            evidence.FailureCodes.ToArray(),
            "latency_histogram_overflow");
    }

    [TestMethod]
    public async Task Transport_executor_stops_when_managed_heap_limit_is_exceeded()
    {
        var events = new List<string>();
        var consumer = new RecordingCapacityConsumer(events);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(events, consumer)),
            new RecordingConsumerFactory(consumer),
            new RecordingWorkloadScheduler(events),
            new ManualClock(),
            maximumManagedHeapBytes: 1);

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(KafkaCapacitySampleState.Incomplete, evidence.State);
        CollectionAssert.Contains(
            evidence.FailureCodes.ToArray(),
            "managed_heap_limit_exceeded");
    }

    [TestMethod]
    public async Task Transport_executor_applies_schedule_limit_to_p99_not_single_outlier()
    {
        var oneOutlierEvents = new List<string>();
        var oneOutlierConsumer = new RecordingCapacityConsumer(oneOutlierEvents);
        var oneOutlierExecutor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(
                oneOutlierEvents,
                oneOutlierConsumer)),
            new RecordingConsumerFactory(oneOutlierConsumer),
            new PercentileWorkloadScheduler(outliers: 1),
            new ManualClock(initialTimestampMicroseconds: 1_000),
            maximumScheduleLatencyMicroseconds: 100);

        var oneOutlierEvidence = await oneOutlierExecutor.ExecuteAsync(
            CreateExecutionContext(
                TimeSpan.Zero,
                maximumMessages: 100,
                maximumScheduleLatencyMicroseconds: 100),
            CancellationToken.None);

        CollectionAssert.DoesNotContain(
            oneOutlierEvidence.FailureCodes.ToArray(),
            "schedule_latency_limit_exceeded");

        var twoOutlierEvents = new List<string>();
        var twoOutlierConsumer = new RecordingCapacityConsumer(twoOutlierEvents);
        var twoOutlierExecutor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(
                twoOutlierEvents,
                twoOutlierConsumer)),
            new RecordingConsumerFactory(twoOutlierConsumer),
            new PercentileWorkloadScheduler(outliers: 2),
            new ManualClock(initialTimestampMicroseconds: 1_000),
            maximumScheduleLatencyMicroseconds: 100);

        var twoOutlierEvidence = await twoOutlierExecutor.ExecuteAsync(
            CreateExecutionContext(
                TimeSpan.Zero,
                maximumMessages: 100,
                maximumScheduleLatencyMicroseconds: 100),
            CancellationToken.None);

        CollectionAssert.Contains(
            twoOutlierEvidence.FailureCodes.ToArray(),
            "schedule_latency_limit_exceeded");
    }

    [TestMethod]
    public async Task Transport_executor_uses_consumer_watermarks_for_broker_backlog()
    {
        var events = new List<string>();
        var consumer = new RecordingCapacityConsumer(
            events,
            backlogAtStop: 7,
            backlogAtDrainCompletion: 0);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new RecordingCapacityProducer(events, consumer)),
            new RecordingConsumerFactory(consumer),
            new RecordingWorkloadScheduler(events),
            new ManualClock());

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(7L, evidence.Performance.BrokerOffsetBacklogAtStop);
        Assert.AreEqual(
            0L,
            evidence.Performance.BrokerOffsetBacklogAtDrainCompletion);
    }

    [TestMethod]
    public async Task Transport_executor_excludes_initialization_statistics_from_sample_queue_peak()
    {
        var events = new List<string>();
        var consumer = new RecordingCapacityConsumer(events);
        var producerFactory = new RecordingProducerFactory(
            new RecordingCapacityProducer(events, consumer),
            statisticsJson: "{\"msg_cnt\":999}");
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            producerFactory,
            new RecordingConsumerFactory(consumer),
            new RecordingWorkloadScheduler(events),
            new ManualClock());

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(0L, evidence.Performance.LocalQueueMessages);
        var statistics = executor.SnapshotStatistics();
        Assert.HasCount(1, statistics);
        Assert.AreEqual(evidence.SampleId, statistics[0].SampleId);
        Assert.AreEqual("initialization", statistics[0].Phase);
    }

    [TestMethod]
    public async Task Transport_executor_uses_one_drain_deadline_for_flush_consume_and_close()
    {
        var events = new List<string>();
        var consumer = new StuckCapacityConsumer(events);
        var executor = new KafkaCapacityTransportExecutor(
            new KafkaMessagingOptions { Enabled = true, BootstrapServers = "broker" },
            new RecordingProducerFactory(new AcknowledgingCapacityProducer(events)),
            new RecordingConsumerFactory(consumer),
            new RecordingWorkloadScheduler(events),
            new ManualClock());

        var evidence = await executor.ExecuteAsync(
            CreateExecutionContext(
                TimeSpan.Zero,
                drainTimeout: TimeSpan.FromMilliseconds(30)),
            CancellationToken.None);

        Assert.AreEqual(KafkaCapacitySampleState.Incomplete, evidence.State);
        Assert.IsTrue(consumer.StopObservedExpiredBudget);
        CollectionAssert.Contains(
            evidence.FailureCodes.ToArray(),
            "consumer_close_failed");
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
        Assert.IsGreaterThan(0d, evidence.Performance.ScheduledMessagesPerSecond);
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
                    context.TopicIdentity,
                    "run"),
                CancellationToken.None));

        Assert.AreEqual(1, driver.Calls);
    }

    [TestMethod]
    public async Task Sample_runner_applies_budget_before_checkpoint_and_stops_the_next_tier()
    {
        var driver = new CountingScenarioDriver();
        var checkpointStore = new RecordingCheckpointStore();
        var runner = new KafkaCapacityRunner(
            driver,
            checkpointStore,
            sample => sample with
            {
                PerformanceBudgetPassed = false,
                FailureCodes = ["consumed_rate_budget_not_met"],
            });
        var context = CreateExecutionContext(TimeSpan.Zero);

        var evidence = await runner.ExecuteSamplesAsync(
            [context, context],
            "checkpoint.json",
            KafkaCapacityCheckpoint.Create(
                "build",
                "scenario",
                KafkaCapacityScopeCodes.KafkaTransport,
                context.TopicIdentity,
                "run"),
            CancellationToken.None);

        Assert.AreEqual(1, driver.Calls);
        Assert.AreEqual(1, checkpointStore.Calls);
        Assert.IsFalse(evidence[0].PerformanceBudgetPassed);
        CollectionAssert.Contains(
            evidence[0].FailureCodes.ToArray(),
            "consumed_rate_budget_not_met");
    }

    [TestMethod]
    public async Task Sample_runner_persists_incomplete_evidence_after_workload_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var checkpointStore = new RecordingCheckpointStore();
        var runner = new KafkaCapacityRunner(
            new CancellingScenarioDriver(cancellation),
            checkpointStore);
        var context = CreateExecutionContext(TimeSpan.Zero);

        var evidence = await runner.ExecuteSamplesAsync(
            [context],
            "checkpoint.json",
            KafkaCapacityCheckpoint.Create(
                "build",
                "scenario",
                KafkaCapacityScopeCodes.KafkaTransport,
                context.TopicIdentity,
                "run"),
            cancellation.Token);

        Assert.HasCount(1, evidence);
        Assert.AreEqual(KafkaCapacitySampleState.Incomplete, evidence[0].State);
        CollectionAssert.Contains(evidence[0].FailureCodes.ToArray(), "cancelled");
        Assert.AreEqual(1, checkpointStore.Calls);
        Assert.IsFalse(checkpointStore.ObservedCancellation);
    }

    [TestMethod]
    public async Task Topic_deletion_requires_every_sample_and_a_non_cancelled_run()
    {
        var context = CreateExecutionContext(TimeSpan.Zero);
        var completed = await new RecordingTransportExecutor()
            .ExecuteAsync(context, CancellationToken.None);
        var planned = new[]
        {
            context.Sample,
            context.Sample with { SampleId = "second" },
        };

        Assert.IsFalse(KafkaCapacityRunner.ShouldDeleteTopic(
            deleteRequested: true,
            planned,
            [completed],
            runCancelled: false));
        Assert.IsFalse(KafkaCapacityRunner.ShouldDeleteTopic(
            deleteRequested: true,
            [context.Sample],
            [completed],
            runCancelled: true));
        Assert.IsFalse(KafkaCapacityRunner.ShouldDeleteTopic(
            deleteRequested: true,
            [context.Sample],
            [completed with
            {
                PerformanceBudgetPassed = false,
                FailureCodes = ["consumed_rate_budget_not_met"],
            }],
            runCancelled: false));
        Assert.IsTrue(KafkaCapacityRunner.ShouldDeleteTopic(
            deleteRequested: true,
            [context.Sample],
            [completed],
            runCancelled: false));
    }

    private static KafkaCapacitySampleContext CreateExecutionContext(
        TimeSpan warmup,
        TimeSpan? drainTimeout = null,
        int maximumMessages = 10,
        long maximumScheduleLatencyMicroseconds = 5_000_000)
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
            warmup: warmup,
            duration: TimeSpan.FromSeconds(1),
            drainTimeout: drainTimeout ?? TimeSpan.FromSeconds(1),
            maximumMessages: maximumMessages,
            maximumScheduleLatencyMicroseconds:
                maximumScheduleLatencyMicroseconds);
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

    private sealed class RecordingDriverFactory(
        string scopeCode,
        string? runtimeScopeCode = null) : IKafkaCapacityScenarioDriverFactory
    {
        public string ScopeCode => scopeCode;

        public KafkaCapacityDriverRuntime Create(
            KafkaCapacityConfiguration configuration) =>
            new(
                new ScopeOverrideDriver(runtimeScopeCode ?? scopeCode),
                StatisticsSource: null);
    }

    private sealed class ScopeOverrideDriver(string scopeCode)
        : IKafkaCapacityScenarioDriver
    {
        public string ScopeCode => scopeCode;

        public Task<KafkaCapacitySampleEvidence> ExecuteAsync(
            KafkaCapacitySampleContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProducerFactory(
        IKafkaCapacityProducer producer,
        string? statisticsJson = null) : IKafkaCapacityProducerFactory
    {
        public string? ClientId { get; private set; }

        public IKafkaCapacityProducer Create(
            KafkaMessagingOptions options,
            string clientId,
            Action<string> statisticsHandler)
        {
            ClientId = clientId;
            if (statisticsJson is not null)
            {
                statisticsHandler(statisticsJson);
            }

            return producer;
        }
    }

    private sealed class RecordingConsumerFactory(
        IKafkaCapacityConsumer consumer) : IKafkaCapacityConsumerFactory
    {
        public string? ClientId { get; private set; }

        public int ExpectedPartitions { get; private set; }

        public IKafkaCapacityConsumer Create(
            KafkaMessagingOptions options,
            string consumerGroupId,
            string clientId,
            int expectedPartitions,
            Action<string> statisticsHandler)
        {
            ClientId = clientId;
            ExpectedPartitions = expectedPartitions;
            return consumer;
        }
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

    private sealed class QueueFullThenAcceptingProducer(
        List<string> events,
        RecordingCapacityConsumer consumer) : IKafkaCapacityProducer
    {
        public int Attempts { get; private set; }

        public void Produce(
            string topicName,
            int partition,
            string key,
            byte[] value,
            long globalSequence,
            Action<KafkaCapacityDeliveryReport> deliveryHandler)
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new KafkaException(new Error(ErrorCode.Local_QueueFull));
            }

            events.Add("produce");
            deliveryHandler(new KafkaCapacityDeliveryReport(
                globalSequence,
                Persisted: true,
                ErrorCode: null));
            consumer.EmitAsync(partition, value).GetAwaiter().GetResult();
        }

        public int Flush(TimeSpan timeout)
        {
            events.Add("producer-flush");
            return 0;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AcknowledgingCapacityProducer(
        List<string> events) : IKafkaCapacityProducer
    {
        public void Produce(
            string topicName,
            int partition,
            string key,
            byte[] value,
            long globalSequence,
            Action<KafkaCapacityDeliveryReport> deliveryHandler)
        {
            events.Add("produce");
            deliveryHandler(new KafkaCapacityDeliveryReport(
                globalSequence,
                Persisted: true,
                ErrorCode: null));
        }

        public int Flush(TimeSpan timeout) => 0;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingCapacityConsumer(
        List<string> events,
        bool corruptPayload = false,
        long backlogAtStop = 0,
        long backlogAtDrainCompletion = 0) : IKafkaCapacityConsumer
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask>?
            messageHandler;
        private int backlogCaptureCount;

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

        public Task<KafkaCapacityBrokerBacklogSnapshot> CaptureBacklogAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var capture = Interlocked.Increment(ref backlogCaptureCount);
            return Task.FromResult(new KafkaCapacityBrokerBacklogSnapshot(
                capture == 1 ? backlogAtStop : backlogAtDrainCompletion));
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

    private sealed class StuckCapacityConsumer(
        List<string> events) : IKafkaCapacityConsumer
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StopObservedExpiredBudget { get; private set; }

        public Task Completion => completion.Task;

        public Task StartAsync(
            string topicName,
            Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask> handler,
            CancellationToken cancellationToken)
        {
            events.Add("consumer-start");
            return Task.CompletedTask;
        }

        public Task WaitForAssignmentAsync(CancellationToken cancellationToken)
        {
            events.Add("consumer-assigned");
            return Task.CompletedTask;
        }

        public Task<KafkaCapacityBrokerBacklogSnapshot> CaptureBacklogAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new KafkaCapacityBrokerBacklogSnapshot(1));

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            StopObservedExpiredBudget = cancellationToken.IsCancellationRequested;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            return new KafkaCapacitySchedulingResult(
                1,
                0,
                1,
                1,
                ActiveDurationMicroseconds: 250_000,
                StopReasonCode: null);
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

    private sealed class PercentileWorkloadScheduler(int outliers)
        : IKafkaCapacityWorkloadScheduler
    {
        public async Task<KafkaCapacitySchedulingResult> RunAsync(
            int targetMessagesPerSecond,
            TimeSpan duration,
            int maximumMessages,
            int producerConcurrency,
            Func<KafkaCapacityScheduledMessage, CancellationToken, ValueTask> writeAsync,
            CancellationToken cancellationToken)
        {
            const int count = 100;
            for (var sequence = 0; sequence < count; sequence++)
            {
                var scheduledTimestamp = sequence >= count - outliers
                    ? 0
                    : 999;
                await writeAsync(
                    new KafkaCapacityScheduledMessage(
                        sequence,
                        scheduledTimestamp),
                    cancellationToken);
            }

            return new KafkaCapacitySchedulingResult(
                count,
                0,
                count,
                count,
                ActiveDurationMicroseconds: 1_000_000,
                StopReasonCode: null);
        }
    }

    private sealed class OverflowingLatencyProducer(
        List<string> events,
        RecordingCapacityConsumer consumer,
        ManualClock clock) : IKafkaCapacityProducer
    {
        public void Produce(
            string topicName,
            int partition,
            string key,
            byte[] value,
            long globalSequence,
            Action<KafkaCapacityDeliveryReport> deliveryHandler)
        {
            events.Add("produce");
            clock.Advance(
                KafkaCapacityLatencyHistogram.MaximumMicroseconds + 1);
            deliveryHandler(new KafkaCapacityDeliveryReport(
                globalSequence,
                Persisted: true,
                ErrorCode: null));
            consumer.EmitAsync(partition, value).GetAwaiter().GetResult();
        }

        public int Flush(TimeSpan timeout) => 0;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private sealed class CancellingScenarioDriver(
        CancellationTokenSource cancellation) : IKafkaCapacityScenarioDriver
    {
        public string ScopeCode => KafkaCapacityScopeCodes.KafkaTransport;

        public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
            KafkaCapacitySampleContext context,
            CancellationToken cancellationToken)
        {
            var completed = await new RecordingTransportExecutor()
                .ExecuteAsync(context, cancellationToken);
            cancellation.Cancel();
            return completed with
            {
                State = KafkaCapacitySampleState.Incomplete,
                FailureCodes = ["cancelled"],
            };
        }
    }

    private sealed class RecordingCheckpointStore : IKafkaCapacityCheckpointStore
    {
        public int Calls { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public Task<KafkaCapacityCheckpoint> SaveAsync(
            string path,
            KafkaCapacityCheckpoint checkpoint,
            KafkaCapacitySampleEvidence evidence,
            CancellationToken cancellationToken)
        {
            Calls++;
            ObservedCancellation = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(checkpoint);
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
        bool blockDelays = false,
        long initialTimestampMicroseconds = 0) : IKafkaCapacityClock
    {
        private long timestampMicroseconds = initialTimestampMicroseconds;

        public TaskCompletionSource DelayEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public long GetTimestampMicroseconds() =>
            Volatile.Read(ref timestampMicroseconds);

        public void Advance(long microseconds) =>
            Interlocked.Add(ref timestampMicroseconds, microseconds);

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
