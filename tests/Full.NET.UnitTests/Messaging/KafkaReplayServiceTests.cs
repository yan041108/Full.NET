using Confluent.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>单元测试（消息/Kafka）：Kafka Replay Service。</summary>
[TestClass]
public sealed class KafkaReplayServiceTests
{
    [TestMethod]
    public async Task Explicit_offset_range_processes_only_bounded_messages_without_committing_group_offset()
    {
        var catalog = CreateCatalog();
        var consumer = Substitute.For<IKafkaReplayConsumer>();
        var partition = new TopicPartition("messaging.orders.v1", 0);
        consumer.GetPartitions("messaging.orders.v1", Arg.Any<TimeSpan>()).Returns([partition]);
        consumer.QueryWatermarkOffsets(partition, Arg.Any<TimeSpan>())
            .Returns(new WatermarkOffsets(0, 1_000));
        consumer.Consume(Arg.Any<TimeSpan>())
            .Returns(
                CreateResult(partition, 100),
                CreateResult(partition, 101),
                CreateResult(partition, 102));
        var factory = Substitute.For<IKafkaReplayConsumerFactory>();
        factory.Create(Arg.Any<ConsumerConfig>()).Returns(consumer);
        var processor = Substitute.For<IKafkaReplayMessageProcessor>();
        processor.ProcessAsync(
                "fullnet.messaging.orders",
                Arg.Any<ConsumeResult<string, byte[]>>(),
                Arg.Any<CancellationToken>())
            .Returns(KafkaReplayMessageOutcome.Processed);
        var service = new KafkaReplayService(
            catalog,
            factory,
            processor,
            Options.Create(CreateOptions()));

        var result = await service.ReplayAsync(CreateRequest(100, 101), CancellationToken.None);

        Assert.AreEqual(2, result.ScannedMessages);
        Assert.AreEqual(2, result.ProcessedMessages);
        consumer.Received(1).Assign(Arg.Is<IReadOnlyList<TopicPartitionOffset>>(
            offsets => offsets != null
                && offsets.Count == 1
                && offsets[0] == new TopicPartitionOffset(partition, 100)));
        await consumer.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task Topic_outside_catalog_is_rejected_before_consumer_creation()
    {
        var factory = Substitute.For<IKafkaReplayConsumerFactory>();
        var service = new KafkaReplayService(
            CreateCatalog(),
            factory,
            Substitute.For<IKafkaReplayMessageProcessor>(),
            Options.Create(CreateOptions()));
        var request = new KafkaReplayRequest(
            "messaging.unknown.v1",
            null,
            null,
            0,
            1,
            [0],
            "fullnet.messaging.orders",
            10,
            "invalid catalog topic");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ReplayAsync(request, CancellationToken.None));

        factory.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [TestMethod]
    public async Task Empty_partition_selector_rejects_topics_above_operation_partition_limit()
    {
        var consumer = Substitute.For<IKafkaReplayConsumer>();
        consumer.GetPartitions("messaging.orders.v1", Arg.Any<TimeSpan>())
            .Returns(Enumerable.Range(0, KafkaReplayRequest.MaximumPartitions + 1)
                .Select(index => new TopicPartition("messaging.orders.v1", index))
                .ToArray());
        var factory = Substitute.For<IKafkaReplayConsumerFactory>();
        factory.Create(Arg.Any<ConsumerConfig>()).Returns(consumer);
        var service = new KafkaReplayService(
            CreateCatalog(),
            factory,
            Substitute.For<IKafkaReplayMessageProcessor>(),
            Options.Create(CreateOptions()));
        var request = new KafkaReplayRequest(
            "messaging.orders.v1",
            null,
            null,
            0,
            1,
            [],
            "fullnet.messaging.orders",
            10,
            "reject unbounded partition fanout");

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.ReplayAsync(request, CancellationToken.None));

        consumer.DidNotReceive()
            .QueryWatermarkOffsets(Arg.Any<TopicPartition>(), Arg.Any<TimeSpan>());
    }

    [TestMethod]
    public async Task Time_range_resolves_fixed_start_and_end_offsets_before_assigning()
    {
        var partition = new TopicPartition("messaging.orders.v1", 0);
        var consumer = Substitute.For<IKafkaReplayConsumer>();
        consumer.GetPartitions("messaging.orders.v1", Arg.Any<TimeSpan>()).Returns([partition]);
        consumer.QueryWatermarkOffsets(partition, Arg.Any<TimeSpan>())
            .Returns(new WatermarkOffsets(0, 1_000));
        consumer.OffsetsForTimes(
                Arg.Any<IReadOnlyList<TopicPartitionTimestamp>>(),
                Arg.Any<TimeSpan>())
            .Returns(
                [new TopicPartitionOffset(partition, 10)],
                [new TopicPartitionOffset(partition, 13)]);
        consumer.Consume(Arg.Any<TimeSpan>())
            .Returns(
                CreateResult(partition, 10),
                CreateResult(partition, 11),
                CreateResult(partition, 12));
        var factory = Substitute.For<IKafkaReplayConsumerFactory>();
        factory.Create(Arg.Any<ConsumerConfig>()).Returns(consumer);
        var processor = Substitute.For<IKafkaReplayMessageProcessor>();
        processor.ProcessAsync(
                "fullnet.messaging.orders",
                Arg.Any<ConsumeResult<string, byte[]>>(),
                Arg.Any<CancellationToken>())
            .Returns(KafkaReplayMessageOutcome.Processed);
        var service = new KafkaReplayService(
            CreateCatalog(),
            factory,
            processor,
            Options.Create(CreateOptions()));
        var request = new KafkaReplayRequest(
            "messaging.orders.v1",
            DateTimeOffset.Parse("2026-08-10T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
            null,
            null,
            [0],
            "fullnet.messaging.orders",
            100,
            "repair time range");

        var result = await service.ReplayAsync(request, CancellationToken.None);

        Assert.AreEqual(3, result.ProcessedMessages);
        consumer.Received(1).Assign(Arg.Is<IReadOnlyList<TopicPartitionOffset>>(
            offsets => offsets != null
                && offsets.Count == 1
                && offsets[0] == new TopicPartitionOffset(partition, 10)));
        consumer.Received(2).OffsetsForTimes(
            Arg.Any<IReadOnlyList<TopicPartitionTimestamp>>(),
            Arg.Any<TimeSpan>());
    }

    [TestMethod]
    public async Task Tracked_replay_persists_intent_before_handler_and_confirmation_after()
    {
        var f = TrackedFixture(10, 13, [10, 11, 12]); var order = new List<string>();
        f.Progress.BeforeMessageAsync(0, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call => { order.Add("before:" + call.ArgAt<long>(1)); return Task.CompletedTask; });
        f.Processor.ProcessAsync(Arg.Any<string>(), Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>()).Returns(call => { order.Add("handle:" + call.ArgAt<ConsumeResult<string, byte[]>>(1).Offset.Value); return Task.FromResult(KafkaReplayMessageOutcome.Processed); });
        f.Progress.AfterMessageAsync(0, Arg.Any<long>(), Arg.Any<KafkaReplayConfirmedOutcome>(), Arg.Any<CancellationToken>()).Returns(call => { order.Add("after:" + call.ArgAt<long>(1)); return Task.CompletedTask; });
        var result = await f.Service.ReplayTrackedAsync(CreateRequest(10, 12), null, f.Progress, default);
        Assert.AreEqual(3, result.ProcessedMessages);
        CollectionAssert.AreEqual(new[] { "before:10", "handle:10", "after:10", "before:11", "handle:11", "after:11", "before:12", "handle:12", "after:12" }, order);
        await f.Progress.Received(1).BindSnapshotAsync(Arg.Is<KafkaReplaySnapshot>(item => item != null && item.ClusterId == "cluster-a" && item.TopicId == "topic-a" && item.Ranges[0].EndOffsetExclusive == 13), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tracked_resume_assigns_persisted_next_offset_and_never_expands_to_new_broker_messages()
    {
        var f = TrackedFixture(0, 20, [12, 13]);
        var snapshot = new KafkaReplaySnapshot("cluster-a", "topic-a", [new(0, 10, 13, 12)]);
        var result = await f.Service.ReplayTrackedAsync(CreateRequest(10, 12), snapshot, f.Progress, default);
        Assert.AreEqual(1, result.ProcessedMessages);
        f.Consumer.Received(1).Assign(Arg.Is<IReadOnlyList<TopicPartitionOffset>>(items => items != null && items.Count == 1 && items[0].Offset.Value == 12));
    }

    [TestMethod]
    [DataRow("cluster-other", "topic-a", 0L, 20L)]
    [DataRow("cluster-a", "topic-other", 0L, 20L)]
    [DataRow("cluster-a", "topic-a", 13L, 20L)]
    [DataRow("cluster-a", "topic-a", 0L, 12L)]
    public async Task Changed_resource_or_retained_window_blocks_before_assignment(string cluster, string topic, long low, long high)
    {
        var f = TrackedFixture(low, high, []);
        var snapshot = new KafkaReplaySnapshot(cluster, topic, [new(0, 10, 13, 12)]);
        await Assert.ThrowsExactlyAsync<KafkaReplayWindowUnavailableException>(() => f.Service.ReplayTrackedAsync(CreateRequest(10, 12), snapshot, f.Progress, default));
        f.Consumer.DidNotReceive().Assign(Arg.Any<IReadOnlyList<TopicPartitionOffset>>());
        Assert.IsEmpty(f.Processor.ReceivedCalls());
    }

    [TestMethod]
    public async Task Exact_offset_creation_rejects_retention_clamping_instead_of_silently_losing_messages()
    {
        var f = TrackedFixture(11, 13, []);
        await Assert.ThrowsExactlyAsync<KafkaReplayWindowUnavailableException>(() => f.Service.ReplayTrackedAsync(CreateRequest(10, 12), null, f.Progress, default));
        Assert.IsEmpty(f.Progress.ReceivedCalls()); Assert.IsEmpty(f.Processor.ReceivedCalls());
    }

    [TestMethod]
    public async Task Rejected_tracked_message_stops_before_later_message_and_unknown_outcome_cannot_checkpoint()
    {
        var f = TrackedFixture(10, 13, [10, 11, 12]);
        f.Processor.ProcessAsync(Arg.Any<string>(), Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>()).Returns(KafkaReplayMessageOutcome.Rejected);
        var result = await f.Service.ReplayTrackedAsync(CreateRequest(10, 12), null, f.Progress, default);
        Assert.AreEqual(1, result.ScannedMessages); Assert.AreEqual(1, result.RejectedMessages);
        f = TrackedFixture(10, 13, [10]);
        f.Processor.ProcessAsync(Arg.Any<string>(), Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>()).Returns((KafkaReplayMessageOutcome)999);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ReplayTrackedAsync(CreateRequest(10, 12), null, f.Progress, default));
        await f.Progress.DidNotReceive().AfterMessageAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<KafkaReplayConfirmedOutcome>(), Arg.Any<CancellationToken>());
    }

    private static (KafkaReplayService Service, IKafkaReplayConsumer Consumer, IKafkaReplayMessageProcessor Processor, IKafkaReplayProgressSink Progress) TrackedFixture(long low, long high, long[] offsets)
    {
        var consumer = Substitute.For<IKafkaReplayConsumer>();
        var partition = new TopicPartition("messaging.orders.v1", 0);
        consumer.GetPartitions(partition.Topic, Arg.Any<TimeSpan>()).Returns([partition]);
        consumer.ReadResourceIdentityAsync(partition.Topic, Arg.Any<CancellationToken>()).Returns(("cluster-a", "topic-a"));
        consumer.QueryWatermarkOffsets(partition, Arg.Any<TimeSpan>()).Returns(new WatermarkOffsets(low, high));
        var queue = new Queue<ConsumeResult<string, byte[]>>(offsets.Select(offset => CreateResult(partition, offset)));
        consumer.Consume(Arg.Any<TimeSpan>()).Returns(_ => queue.Count > 0 ? queue.Dequeue() : new ConsumeResult<string, byte[]> { Topic = partition.Topic, Partition = 0, Offset = high, IsPartitionEOF = true });
        var factory = Substitute.For<IKafkaReplayConsumerFactory>(); factory.Create(Arg.Any<ConsumerConfig>()).Returns(consumer);
        var processor = Substitute.For<IKafkaReplayMessageProcessor>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<ConsumeResult<string, byte[]>>(), Arg.Any<CancellationToken>()).Returns(KafkaReplayMessageOutcome.Processed);
        return (new(CreateCatalog(), factory, processor, Options.Create(CreateOptions())), consumer, processor, Substitute.For<IKafkaReplayProgressSink>());
    }

    private static KafkaReplayRequest CreateRequest(long from, long to) =>
        new(
            "messaging.orders.v1",
            null,
            null,
            from,
            to,
            [0],
            "fullnet.messaging.orders",
            100,
            "repair projection gap");

    private static KafkaMessagingOptions CreateOptions() =>
        new()
        {
            BootstrapServers = "localhost:9092",
            SecurityProtocol = "Plaintext",
            ClientId = "fullnet.messaging.replay.test",
        };

    private static IntegrationEventSubscriptionCatalog CreateCatalog() =>
        new(
            [IntegrationEventTopicDefinition.Create(
                "messaging.orders.v1",
                "fullnet.messaging.order.changed",
                1,
                EventDeliveryOwner.CdcKafka)],
            []);

    private static ConsumeResult<string, byte[]> CreateResult(
        TopicPartition partition,
        long offset) =>
        new()
        {
            Topic = partition.Topic,
            Partition = partition.Partition,
            Offset = offset,
            Message = new Message<string, byte[]> { Key = "order-1", Value = [0x01] },
        };
}
