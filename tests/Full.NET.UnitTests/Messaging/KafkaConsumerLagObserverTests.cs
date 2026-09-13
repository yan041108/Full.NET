using Full.NET.Messaging.Kafka;
using Confluent.Kafka;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaConsumerLagObserverTests
{
    [TestMethod]
    public async Task Delayed_query_is_awaited_and_client_is_owned_until_completion()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<TopicPartition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(pending.Task);
        var partition = new TopicPartition("topic", 0);
        client.ReadCommittedOffsetsAsync("group", Arg.Any<IReadOnlyList<TopicPartition>>())
            .Returns(new Dictionary<TopicPartition, long> { [partition] = 3 });
        client.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>())
            .Returns(new Dictionary<TopicPartition, long> { [partition] = 10 });
        var observer = new KafkaConsumerLagObserver(_ => client);
        var task = observer.ObserveLagAsync(new(), "topic", "group", CancellationToken.None);
        Assert.IsFalse(task.IsCompleted);
        client.DidNotReceive().Dispose();
        pending.SetResult([partition]);
        Assert.AreEqual(7L, (await task)!.TotalLagMessages);
        client.Received(1).Dispose();
    }

    [TestMethod]
    public async Task Cancellation_waits_for_sdk_request_then_disposes_without_starting_next_request()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<TopicPartition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(pending.Task);
        using var cancellation = new CancellationTokenSource();
        var task = new KafkaConsumerLagObserver(_ => client).ObserveLagAsync(new(), "topic", "group", cancellation.Token);
        cancellation.Cancel();
        Assert.IsFalse(task.IsCompleted);
        pending.SetResult([new TopicPartition("topic", 0)]);
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        client.Received(1).Dispose();
        await client.DidNotReceive().ReadCommittedOffsetsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TopicPartition>>());
    }

    [TestMethod]
    public async Task Missing_partition_data_is_not_proof_of_drain()
    {
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(Array.Empty<TopicPartition>());
        Assert.IsNull(await new KafkaConsumerLagObserver(_ => client).ObserveLagAsync(new(), "topic", "group", CancellationToken.None));
        client.Received(1).Dispose();
    }

    [TestMethod]
    public async Task Missing_high_watermark_is_not_proof_of_drain()
    {
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(new[] { new TopicPartition("topic", 0) });
        client.ReadCommittedOffsetsAsync("group", Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long>());
        client.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long>());
        Assert.IsNull(await new KafkaConsumerLagObserver(_ => client).ObserveLagAsync(new(), "topic", "group", CancellationToken.None));
    }

    [TestMethod]
    public async Task Unknown_committed_offset_counts_full_high_watermark()
    {
        var client = Substitute.For<IKafkaLagQueryClient>();
        var partition = new TopicPartition("topic", 0);
        client.ReadPartitionsAsync("topic").Returns(new[] { partition });
        client.ReadCommittedOffsetsAsync("group", Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long>());
        client.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long> { [partition] = 10 });
        Assert.AreEqual(10L, (await new KafkaConsumerLagObserver(_ => client).ObserveLagAsync(new(), "topic", "group", CancellationToken.None))!.TotalLagMessages);
    }

    [TestMethod]
    public async Task Request_failure_disposes_client_and_returns_unknown()
    {
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(Task.FromException<IReadOnlyList<TopicPartition>>(new IOException("探针失败")));
        Assert.IsNull(await new KafkaConsumerLagObserver(_ => client).ObserveLagAsync(new(), "topic", "group", CancellationToken.None));
        client.Received(1).Dispose();
    }

    [TestMethod]
    public async Task Drain_deadline_returns_false_instead_of_starting_final_probe()
    {
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(Array.Empty<TopicPartition>());
        var observer = new KafkaConsumerLagObserver(_ => client);
        Assert.IsFalse(await observer.WaitUntilDrainedAsync(new(), "topic", "group", TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(10), CancellationToken.None));
    }
}
