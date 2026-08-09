using Confluent.Kafka;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaConsumerPartitionCoordinatorTests
{
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

    private static KafkaConsumerPartitionCoordinator CreateCoordinator(
        IConsumer<string, byte[]> consumer,
        KafkaPartitionWorkScheduler scheduler) =>
        new(
            consumer,
            scheduler,
            new KafkaMessagingOptions
            {
                UncommittedRetryBackoffMilliseconds = 100,
            },
            "fullnet.messaging.test",
            NullLogger.Instance);

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

    private static ConsumeResult<string, byte[]> CreateResult(
        TopicPartition topicPartition,
        long offset) =>
        new()
        {
            Topic = topicPartition.Topic,
            Partition = topicPartition.Partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = "aggregate-1",
                Value = [0x01],
            },
        };
}
