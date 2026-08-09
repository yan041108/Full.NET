using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class KafkaSubscriptionTests
{
    [TestMethod]
    public async Task Two_consumer_groups_both_receive_the_same_event()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.subscription.fanout.{Guid.NewGuid():N}.v1";
        var payload = new byte[] { 0x01 };
        var message = KafkaTestMessages.Create(topic, "partition-a", payload);

        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");
        await producer.ProduceAsync(topic, message).ConfigureAwait(false);
        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumerA = environment.CreateConsumer("fullnet.kafka.test.group-a", "fullnet.kafka.test.consumer-a");
        using var consumerB = environment.CreateConsumer("fullnet.kafka.test.group-b", "fullnet.kafka.test.consumer-b");
        consumerA.Subscribe(topic);
        consumerB.Subscribe(topic);

        var resultA = await KafkaTestMessages.ConsumeOneAsync(consumerA, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var resultB = await KafkaTestMessages.ConsumeOneAsync(consumerB, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        CollectionAssert.AreEqual(payload, resultA.Message.Value);
        CollectionAssert.AreEqual(payload, resultB.Message.Value);
    }

    [TestMethod]
    public async Task Same_consumer_group_competes_so_only_one_instance_processes_each_message()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.subscription.compete.{Guid.NewGuid():N}.v1";
        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");

        for (var index = 0; index < 6; index++)
        {
            await producer.ProduceAsync(
                    topic,
                    KafkaTestMessages.Create(topic, "key-shared", [ (byte)index ]))
                .ConfigureAwait(false);
        }

        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumerA = environment.CreateConsumer("fullnet.kafka.test.compete", "fullnet.kafka.test.compete-a");
        using var consumerB = environment.CreateConsumer("fullnet.kafka.test.compete", "fullnet.kafka.test.compete-b");
        consumerA.Subscribe(topic);
        consumerB.Subscribe(topic);

        var received = new List<byte>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (received.Count < 6 && DateTime.UtcNow < deadline)
        {
            var fromA = consumerA.Consume(TimeSpan.FromMilliseconds(200));
            if (fromA?.Message?.Value is { Length: > 0 } valueA)
            {
                received.Add(valueA[0]);
                consumerA.Commit(fromA);
            }

            var fromB = consumerB.Consume(TimeSpan.FromMilliseconds(200));
            if (fromB?.Message?.Value is { Length: > 0 } valueB)
            {
                received.Add(valueB[0]);
                consumerB.Commit(fromB);
            }
        }

        CollectionAssert.AreEquivalent(new byte[] { 0, 1, 2, 3, 4, 5 }, received);
        Assert.AreEqual(6, received.Count);
    }

    [TestMethod]
    public async Task Same_partition_key_preserves_order_within_partition()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.subscription.order.{Guid.NewGuid():N}.v1";
        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");

        for (var index = 0; index < 5; index++)
        {
            await producer.ProduceAsync(
                    topic,
                    KafkaTestMessages.Create(topic, "ordered-key", [ (byte)index ]))
                .ConfigureAwait(false);
        }

        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumer = environment.CreateConsumer("fullnet.kafka.test.order", "fullnet.kafka.test.order");
        consumer.Subscribe(topic);

        var ordered = new List<byte>();
        for (var index = 0; index < 5; index++)
        {
            var result = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            ordered.Add(result.Message.Value![0]);
            consumer.Commit(result);
        }

        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3, 4 }, ordered);
    }

    [TestMethod]
    public async Task Manual_commit_advances_offset_only_after_explicit_commit()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.subscription.commit.{Guid.NewGuid():N}.v1";
        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");
        await producer.ProduceAsync(topic, KafkaTestMessages.Create(topic, "commit-key", [0x7A])).ConfigureAwait(false);
        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumer = environment.CreateConsumer("fullnet.kafka.test.commit", "fullnet.kafka.test.commit");
        consumer.Subscribe(topic);

        var first = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        Assert.AreEqual(0x7A, first.Message.Value![0]);

        var redelivered = consumer.Consume(TimeSpan.FromMilliseconds(500));
        Assert.IsNotNull(redelivered);
        Assert.AreEqual(0x7A, redelivered!.Message.Value![0]);

        consumer.Commit(first);
        var afterCommit = consumer.Consume(TimeSpan.FromMilliseconds(500));
        Assert.IsNull(afterCommit);
    }
}

internal static class KafkaTestMessages
{
    internal static Message<string, byte[]> Create(string topic, string partitionKey, byte[] payload)
    {
        var eventId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        return new Message<string, byte[]>
        {
            Key = partitionKey,
            Value = payload,
            Headers =
            [
                new Header(KafkaEnvelopeHeaderNames.EventId, Encoding.UTF8.GetBytes(eventId.ToString("D"))),
                new Header(KafkaEnvelopeHeaderNames.MessageType, Encoding.UTF8.GetBytes("fullnet.messaging.kafka.test.event")),
                new Header(KafkaEnvelopeHeaderNames.SchemaVersion, Encoding.UTF8.GetBytes("1")),
                new Header(KafkaEnvelopeHeaderNames.ContentType, Encoding.UTF8.GetBytes(MessagingNames.ContentTypeMessagePack)),
                new Header(KafkaEnvelopeHeaderNames.Producer, Encoding.UTF8.GetBytes("fullnet.messaging.tests")),
                new Header(KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(occurredAt.ToString("O"))),
            ],
        };
    }

    internal static async Task<ConsumeResult<string, byte[]>> ConsumeOneAsync(
        IConsumer<string, byte[]> consumer,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, byte[]>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromMilliseconds(200));
            }
            catch (ConsumeException exception) when (!exception.Error.IsFatal)
            {
                // 自动建 Topic 和元数据传播之间存在短窗口；可恢复错误不应让测试误判为消息丢失。
                await Task.Delay(50).ConfigureAwait(false);
                continue;
            }

            if (result?.Message?.Value is not null)
            {
                return result;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail("Timed out waiting for Kafka message.");
        throw new InvalidOperationException();
    }
}
