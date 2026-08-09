using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaPartitionWorkSchedulerTests
{
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

    private static ConsumeResult<string, byte[]> CreateResult(int partition, long offset) =>
        new()
        {
            Topic = "fullnet.test.events.v1",
            Partition = partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = $"aggregate-{partition}",
                Value = [0x01],
            },
        };
}
