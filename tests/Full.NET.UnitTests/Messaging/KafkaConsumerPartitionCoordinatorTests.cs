using Confluent.Kafka;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaConsumerPartitionCoordinatorTests
{
    [TestMethod]
    public async Task Partition_resumes_only_after_buffer_reaches_low_watermark()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 8);
        var consumer = CreateConsumer(partition);
        var releases = new[] { NewSignal(), NewSignal() };
        var started = new[] { NewSignal(), NewSignal() };
        var call = 0;
        var options = CreateParallelOptions();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                var index = Interlocked.Increment(ref call) - 1;
                started[index].TrySetResult();
                await releases[index].Task.WaitAsync(cancellationToken);
                return true;
            },
            options);
        var coordinator = CreateCoordinator(consumer, scheduler, options, DateTimeOffset.UtcNow);
        coordinator.OnAssigned([partition]);
        var keys = FindKeysForDifferentSlots(2);

        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 80, keys[0])));
        consumer.DidNotReceiveWithAnyArgs().Pause(default(IEnumerable<TopicPartition>)!);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 81, keys[1])));
        consumer.Received(1).Pause(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { partition })));
        await Task.WhenAll(started.Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(2));

        releases[0].TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);

        consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { partition })));
        releases[1].TrySetResult();
    }

    [TestMethod]
    public async Task Global_high_watermark_pauses_all_assigned_partitions_and_low_watermark_resumes_them()
    {
        var first = new TopicPartition("fullnet.test.events.v1", 9);
        var second = new TopicPartition("fullnet.test.events.v1", 10);
        var consumer = CreateConsumer(first, second);
        var releases = new[] { NewSignal(), NewSignal() };
        var call = 0;
        var options = CreateParallelOptions();
        options.ConsumerBufferHighWatermark = 2;
        options.ConsumerBufferLowWatermark = 1;
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                var index = Interlocked.Increment(ref call) - 1;
                await releases[index].Task.WaitAsync(cancellationToken);
                return true;
            },
            options);
        var coordinator = CreateCoordinator(consumer, scheduler, options, DateTimeOffset.UtcNow);
        coordinator.OnAssigned([first, second]);

        Assert.IsTrue(coordinator.TryDispatch(CreateResult(first, 90)));
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(second, 100)));
        consumer.Received().Pause(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null
                && partitions.OrderBy(item => item.Partition.Value)
                    .SequenceEqual(new[] { first, second })));

        releases[0].TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);

        consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { first })));
        consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { second })));
        releases[1].TrySetResult();
    }

    [TestMethod]
    public async Task Dispatch_pauses_only_the_partition_being_processed()
    {
        var first = new TopicPartition("fullnet.test.events.v1", 0);
        var second = new TopicPartition("fullnet.test.events.v1", 1);
        var consumer = CreateConsumer(first, second);
        var release = NewSignal();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                return true;
            });
        var coordinator = CreateCoordinator(consumer, scheduler);
        coordinator.OnAssigned([first, second]);

        Assert.IsTrue(coordinator.TryDispatch(CreateResult(first, 7)));

        consumer.Received(1).Pause(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { first })));
        consumer.DidNotReceive().Pause(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.Contains(second)));
        release.TrySetResult();
    }

    [TestMethod]
    public async Task Successful_completion_commits_next_offset_then_resumes_its_partition()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 2);
        var consumer = CreateConsumer(partition);
        await using var scheduler = new KafkaPartitionWorkScheduler((_, _) => Task.FromResult(true));
        var coordinator = CreateCoordinator(consumer, scheduler);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 19)));
        await WaitForCompletionAsync(scheduler);

        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);

        consumer.Received(1).Commit(Arg.Is<IEnumerable<TopicPartitionOffset>>(
            offsets => offsets != null && offsets.SequenceEqual(
                new[] { new TopicPartitionOffset(partition, 20) })));
        consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { partition })));
    }

    [TestMethod]
    public async Task Failed_completion_seeks_current_offset_and_resumes_only_after_backoff()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 3);
        var consumer = CreateConsumer(partition);
        await using var scheduler = new KafkaPartitionWorkScheduler((_, _) => Task.FromResult(false));
        var coordinator = CreateCoordinator(consumer, scheduler);
        coordinator.OnAssigned([partition]);
        var message = CreateResult(partition, 31);
        Assert.IsTrue(coordinator.TryDispatch(message));
        await WaitForCompletionAsync(scheduler);
        var now = DateTimeOffset.UtcNow;

        coordinator.ProcessCompletions(now);
        coordinator.ResumeDuePartitions(now.AddMilliseconds(99));

        consumer.Received(1).Seek(message.TopicPartitionOffset);
        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);
        consumer.DidNotReceiveWithAnyArgs().Resume(default(IEnumerable<TopicPartition>)!);

        coordinator.ResumeDuePartitions(now.AddMilliseconds(100));
        consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { partition })));
    }

    [TestMethod]
    public async Task Completion_from_revoked_assignment_never_commits_or_resumes()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 4);
        var consumer = CreateConsumer(partition);
        var started = NewSignal();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            });
        var coordinator = CreateCoordinator(consumer, scheduler);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 40)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.OnRevoked([partition]);
        await WaitForCompletionAsync(scheduler);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);

        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);
        consumer.DidNotReceiveWithAnyArgs().Resume(default(IEnumerable<TopicPartition>)!);
    }

    [TestMethod]
    public async Task Periodic_mode_resumes_processing_but_commits_only_when_interval_is_due()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 5);
        var consumer = CreateConsumer(partition);
        await using var scheduler = new KafkaPartitionWorkScheduler((_, _) => Task.FromResult(true));
        var now = DateTimeOffset.UtcNow;
        var coordinator = CreateCoordinator(
            consumer,
            scheduler,
            new KafkaMessagingOptions
            {
                OffsetCommitMode = KafkaOffsetCommitMode.PeriodicWatermark,
                OffsetCommitIntervalMilliseconds = 1_000,
                OffsetCommitBatchSize = 100,
                UncommittedRetryBackoffMilliseconds = 100,
            },
            now);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 50)));
        await WaitForCompletionAsync(scheduler);

        coordinator.ProcessCompletions(now.AddMilliseconds(999));

        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);
        consumer.Received(1).Resume(Arg.Is<IEnumerable<TopicPartition>>(
            partitions => partitions != null && partitions.SequenceEqual(new[] { partition })));

        coordinator.ProcessCompletions(now.AddSeconds(1));
        consumer.Received(1).Commit(Arg.Is<IEnumerable<TopicPartitionOffset>>(
            offsets => offsets != null && offsets.SequenceEqual(
                new[] { new TopicPartitionOffset(partition, 51) })));
    }

    [TestMethod]
    public async Task Revoked_partition_force_flushes_its_pending_safe_watermark()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 6);
        var consumer = CreateConsumer(partition);
        await using var scheduler = new KafkaPartitionWorkScheduler((_, _) => Task.FromResult(true));
        var now = DateTimeOffset.UtcNow;
        var coordinator = CreateCoordinator(
            consumer,
            scheduler,
            new KafkaMessagingOptions
            {
                OffsetCommitMode = KafkaOffsetCommitMode.PeriodicWatermark,
                OffsetCommitIntervalMilliseconds = 1_000,
                OffsetCommitBatchSize = 100,
            },
            now);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 60)));
        await WaitForCompletionAsync(scheduler);
        coordinator.ProcessCompletions(now);

        coordinator.OnRevoked([partition]);

        consumer.Received(1).Commit(Arg.Is<IEnumerable<TopicPartitionOffset>>(
            offsets => offsets != null && offsets.SequenceEqual(
                new[] { new TopicPartitionOffset(partition, 61) })));
    }

    [TestMethod]
    public async Task Lost_partition_discards_pending_watermark_without_committing()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 7);
        var consumer = CreateConsumer(partition);
        await using var scheduler = new KafkaPartitionWorkScheduler((_, _) => Task.FromResult(true));
        var now = DateTimeOffset.UtcNow;
        var coordinator = CreateCoordinator(
            consumer,
            scheduler,
            new KafkaMessagingOptions
            {
                OffsetCommitMode = KafkaOffsetCommitMode.PeriodicWatermark,
                OffsetCommitIntervalMilliseconds = 1_000,
                OffsetCommitBatchSize = 100,
            },
            now);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 70)));
        await WaitForCompletionAsync(scheduler);
        coordinator.ProcessCompletions(now);

        coordinator.OnLost([partition]);
        coordinator.ProcessCompletions(now.AddSeconds(2));

        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);
    }

    [TestMethod]
    public async Task Revoked_partition_discards_watermark_after_nonfatal_final_commit_failure()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 11);
        var consumer = CreateConsumer(partition);
        consumer.When(item => item.Commit(Arg.Any<IEnumerable<TopicPartitionOffset>>()))
            .Do(_ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut)));
        var now = DateTimeOffset.UtcNow;
        var options = new KafkaMessagingOptions
        {
            OffsetCommitMode = KafkaOffsetCommitMode.PeriodicWatermark,
            OffsetCommitIntervalMilliseconds = 100,
            OffsetCommitBatchSize = 100,
        };
        await using var scheduler = new KafkaPartitionWorkScheduler((_, _) => Task.FromResult(true));
        var coordinator = CreateCoordinator(consumer, scheduler, options, now);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 110)));
        await WaitForCompletionAsync(scheduler);
        coordinator.ProcessCompletions(now);

        coordinator.OnRevoked([partition]);
        coordinator.ProcessCompletions(now.AddSeconds(1));

        consumer.Received(1).Commit(Arg.Any<IEnumerable<TopicPartitionOffset>>());
    }

    [TestMethod]
    public async Task Retry_epoch_rejects_old_lane_completion_with_same_offset_as_redelivery()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 12);
        var consumer = CreateConsumer(partition);
        var keys = FindKeysForDifferentSlots(2);
        var oldLaterStarted = NewSignal();
        var releaseOldLater = NewSignal();
        var releaseNewFirst = NewSignal();
        var releaseNewLater = NewSignal();
        var offsetCalls = new Dictionary<long, int>();
        var sync = new object();
        var options = CreateParallelOptions();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (message, _) =>
            {
                int call;
                lock (sync)
                {
                    offsetCalls.TryGetValue(message.Offset.Value, out var current);
                    call = current + 1;
                    offsetCalls[message.Offset.Value] = call;
                }

                if (message.Offset.Value == 120 && call == 1)
                {
                    await oldLaterStarted.Task;
                    return false;
                }

                if (message.Offset.Value == 121 && call == 1)
                {
                    oldLaterStarted.TrySetResult();
                    await releaseOldLater.Task;
                    return true;
                }

                await (message.Offset.Value == 120
                    ? releaseNewFirst.Task
                    : releaseNewLater.Task);
                return true;
            },
            options);
        var coordinator = CreateCoordinator(consumer, scheduler, options, DateTimeOffset.UtcNow);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 120, keys[0])));
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 121, keys[1])));
        await oldLaterStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForAnyCompletionAsync(scheduler);
        var now = DateTimeOffset.UtcNow;
        coordinator.ProcessCompletions(now);

        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 120, keys[0])));
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 121, keys[1])));
        releaseOldLater.TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(now.AddMilliseconds(1));
        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);

        releaseNewLater.TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(now.AddMilliseconds(2));
        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);

        releaseNewFirst.TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(now.AddMilliseconds(3));
        consumer.Received(1).Commit(Arg.Is<IEnumerable<TopicPartitionOffset>>(
            offsets => offsets != null && offsets.SequenceEqual(
                new[] { new TopicPartitionOffset(partition, 122) })));
    }

    [TestMethod]
    public async Task Later_failure_seeks_earliest_pending_offset_before_restarting_epoch()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 13);
        var consumer = CreateConsumer(partition);
        var keys = FindKeysForDifferentSlots(2);
        var earlierStarted = NewSignal();
        var laterStarted = NewSignal();
        var releaseEarlier = NewSignal();
        var releaseLaterFailure = NewSignal();
        var options = CreateParallelOptions();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (message, _) =>
            {
                if (message.Offset.Value == 130)
                {
                    earlierStarted.TrySetResult();
                    await releaseEarlier.Task;
                    return false;
                }

                laterStarted.TrySetResult();
                await releaseLaterFailure.Task;
                return false;
            },
            options);
        var coordinator = CreateCoordinator(
            consumer,
            scheduler,
            options,
            DateTimeOffset.UtcNow);
        coordinator.OnAssigned([partition]);
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 130, keys[0])));
        Assert.IsTrue(coordinator.TryDispatch(CreateResult(partition, 131, keys[1])));
        await Task.WhenAll(earlierStarted.Task, laterStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(2));

        releaseLaterFailure.TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);

        consumer.Received(1).Seek(new TopicPartitionOffset(partition, 130));
        releaseEarlier.TrySetResult();
        await WaitForAnyCompletionAsync(scheduler);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow.AddMilliseconds(1));
        consumer.DidNotReceiveWithAnyArgs().Commit(default(IEnumerable<TopicPartitionOffset>)!);
    }

    private static KafkaConsumerPartitionCoordinator CreateCoordinator(
        IConsumer<string, byte[]> consumer,
        KafkaPartitionWorkScheduler scheduler) =>
        CreateCoordinator(
            consumer,
            scheduler,
            new KafkaMessagingOptions
            {
                UncommittedRetryBackoffMilliseconds = 100,
            },
            DateTimeOffset.UtcNow);

    private static KafkaConsumerPartitionCoordinator CreateCoordinator(
        IConsumer<string, byte[]> consumer,
        KafkaPartitionWorkScheduler scheduler,
        KafkaMessagingOptions options,
        DateTimeOffset initialUtc) =>
        new(
            consumer,
            scheduler,
            options,
            "fullnet.messaging.test",
            NullLogger.Instance,
            initialUtc);

    private static IConsumer<string, byte[]> CreateConsumer(
        params TopicPartition[] assignment)
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Assignment.Returns(assignment.ToList());
        return consumer;
    }

    private static async Task WaitForCompletionAsync(KafkaPartitionWorkScheduler scheduler)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while ((scheduler.InFlightCount != 0 || !scheduler.HasPendingCompletion)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.AreEqual(0, scheduler.InFlightCount);
        Assert.IsTrue(scheduler.HasPendingCompletion);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static KafkaMessagingOptions CreateParallelOptions() =>
        new()
        {
            ConsumerBufferHighWatermark = 8,
            ConsumerBufferLowWatermark = 4,
            PartitionBufferHighWatermark = 2,
            PartitionBufferLowWatermark = 1,
            PartitionKeyConcurrencySlots = 2,
            UncommittedRetryBackoffMilliseconds = 100,
        };

    private static string[] FindKeysForDifferentSlots(int slotCount) =>
        Enumerable.Range(0, 100)
            .Select(index => $"aggregate-{index}")
            .GroupBy(key => KafkaPartitionKeySlotSelector.SelectSlot(key, slotCount))
            .Select(group => group.First())
            .Take(slotCount)
            .ToArray();

    private static async Task WaitForAnyCompletionAsync(KafkaPartitionWorkScheduler scheduler)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!scheduler.HasPendingCompletion && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(scheduler.HasPendingCompletion);
    }

    private static ConsumeResult<string, byte[]> CreateResult(
        TopicPartition topicPartition,
        long offset,
        string key = "aggregate-1") =>
        new()
        {
            Topic = topicPartition.Topic,
            Partition = topicPartition.Partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = key,
                Value = [0x01],
            },
        };
}
