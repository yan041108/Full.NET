using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaPartitionWorkSchedulerTests
{
    [TestMethod]
    public async Task Same_partition_runs_different_key_slots_in_parallel()
    {
        var keys = Enumerable.Range(0, 100)
            .Select(index => $"aggregate-{index}")
            .GroupBy(key => KafkaPartitionKeySlotSelector.SelectSlot(key, 2))
            .Select(group => group.First())
            .Take(2)
            .ToArray();
        Assert.HasCount(2, keys);
        var started = new[] { NewSignal(), NewSignal() };
        var release = NewSignal();
        var call = 0;
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                started[Interlocked.Increment(ref call) - 1].TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return true;
            },
            CreateParallelOptions());

        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 10, keys[0]), assignmentEpoch: 1));
        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 11, keys[1]), assignmentEpoch: 1));
        await Task.WhenAll(started.Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(2));

        release.TrySetResult();
        Assert.HasCount(2, await ReadCompletionsAsync(scheduler, 2));
    }

    [TestMethod]
    public async Task Same_key_is_serial_even_when_partition_has_multiple_slots()
    {
        var firstStarted = NewSignal();
        var secondStarted = NewSignal();
        var releaseFirst = NewSignal();
        var call = 0;
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref call) == 1)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondStarted.TrySetResult();
                }

                return true;
            },
            CreateParallelOptions());

        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 20, "same-key"), assignmentEpoch: 1));
        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 21, "same-key"), assignmentEpoch: 1));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => secondStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(100)));

        releaseFirst.TrySetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.HasCount(2, await ReadCompletionsAsync(scheduler, 2));
    }

    [TestMethod]
    public async Task Failed_message_cancels_queued_same_key_before_it_enters_handler()
    {
        var firstStarted = NewSignal();
        var releaseFailure = NewSignal();
        var calls = 0;
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref calls);
                firstStarted.TrySetResult();
                await releaseFailure.Task.WaitAsync(cancellationToken);
                return false;
            },
            CreateParallelOptions());

        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 30, "same-key"), assignmentEpoch: 1));
        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 31, "same-key"), assignmentEpoch: 1));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseFailure.TrySetResult();

        var completion = await scheduler.ReadCompletionAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (scheduler.InFlightCount != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsFalse(completion.ShouldCommit);
        Assert.AreEqual(1, Volatile.Read(ref calls));
        Assert.AreEqual(0, scheduler.InFlightCount);
    }

    [TestMethod]
    public async Task Different_partitions_run_in_parallel_while_each_partition_has_one_bounded_slot()
    {
        var firstStarted = NewSignal();
        var secondStarted = NewSignal();
        var release = NewSignal();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (message, cancellationToken) =>
            {
                (message.Partition.Value == 0 ? firstStarted : secondStarted).TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return true;
            });
        var first = CreateResult(0, 10);
        var samePartition = CreateResult(0, 11);
        var otherPartition = CreateResult(1, 20);

        Assert.IsTrue(scheduler.TrySchedule(first, assignmentEpoch: 1));
        Assert.IsFalse(scheduler.TrySchedule(samePartition, assignmentEpoch: 1));
        Assert.IsTrue(scheduler.TrySchedule(otherPartition, assignmentEpoch: 2));
        await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(2));

        release.TrySetResult();
        var completions = await ReadCompletionsAsync(scheduler, 2);

        Assert.AreEqual(2, completions.Count);
        Assert.IsTrue(completions.All(result => result.ShouldCommit));
    }

    [TestMethod]
    public async Task Processing_state_callback_observes_handler_start_and_completion()
    {
        var started = NewSignal();
        var release = NewSignal();
        var states = new System.Collections.Concurrent.ConcurrentQueue<(int Inflight, int BufferDepth)>();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return true;
            },
            new KafkaMessagingOptions(),
            (_, inflight, bufferDepth) => states.Enqueue((inflight, bufferDepth)));

        Assert.IsTrue(scheduler.TrySchedule(CreateResult(0, 40), assignmentEpoch: 1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(states.Any(state => state == (1, 0)));

        release.TrySetResult();
        await scheduler.ReadCompletionAsync(CancellationToken.None);
        Assert.IsTrue(states.Any(state => state == (0, 0)));
    }

    [TestMethod]
    public async Task Revoked_partition_cancels_inflight_work_and_reports_its_original_epoch()
    {
        var started = NewSignal();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            });
        var message = CreateResult(3, 42);

        Assert.IsTrue(scheduler.TrySchedule(message, assignmentEpoch: 7));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        scheduler.Revoke(message.TopicPartition);
        var completion = await scheduler.ReadCompletionAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(7L, completion.AssignmentEpoch);
        Assert.IsFalse(completion.ShouldCommit);
        Assert.IsInstanceOfType<OperationCanceledException>(completion.Exception);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (scheduler.TrackedLaneTaskCount != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.AreEqual(
            0,
            scheduler.TrackedLaneTaskCount,
            "已撤销并完成的分区 Lane 不得永久残留在任务跟踪集合中。");
    }

    [TestMethod]
    public async Task Dispose_does_not_bypass_bounded_shutdown_when_handler_ignores_cancellation()
    {
        var started = NewSignal();
        var release = NewSignal();
        var scheduler = new KafkaPartitionWorkScheduler(
            async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return true;
            });
        Assert.IsTrue(scheduler.TrySchedule(CreateResult(5, 50), assignmentEpoch: 11));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var drained = await scheduler.StopAsync(TimeSpan.FromMilliseconds(50));
        Assert.IsFalse(drained);

        try
        {
            await scheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<IReadOnlyList<KafkaPartitionProcessingResult>> ReadCompletionsAsync(
        KafkaPartitionWorkScheduler scheduler,
        int count)
    {
        var results = new List<KafkaPartitionProcessingResult>(count);
        while (results.Count < count)
        {
            results.Add(await scheduler.ReadCompletionAsync(CancellationToken.None));
        }

        return results;
    }

    private static KafkaMessagingOptions CreateParallelOptions() =>
        new()
        {
            ConsumerBufferHighWatermark = 8,
            ConsumerBufferLowWatermark = 4,
            PartitionBufferHighWatermark = 4,
            PartitionBufferLowWatermark = 1,
            PartitionKeyConcurrencySlots = 2,
        };

    private static ConsumeResult<string, byte[]> CreateResult(
        int partition,
        long offset,
        string? key = null) =>
        new()
        {
            Topic = "fullnet.test.events.v1",
            Partition = partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = key ?? $"aggregate-{partition}",
                Value = [0x01],
            },
        };
}
