using Confluent.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

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
