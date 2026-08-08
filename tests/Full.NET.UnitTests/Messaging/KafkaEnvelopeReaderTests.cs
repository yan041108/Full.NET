using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaEnvelopeReaderTests
{
    private static readonly byte[] SamplePayload = [0x01, 0x02, 0x03];

    [TestMethod]
    public void TryRead_maps_headers_key_and_value_to_envelope()
    {
        var eventId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        var consumeResult = CreateConsumeResult(
            partitionKey: "tenant-42",
            payload: SamplePayload,
            new Dictionary<string, string>
            {
                [KafkaEnvelopeHeaderNames.EventId] = eventId.ToString("D"),
                [KafkaEnvelopeHeaderNames.MessageType] = "fullnet.messaging.kafka.test.event",
                [KafkaEnvelopeHeaderNames.SchemaVersion] = "1",
                [KafkaEnvelopeHeaderNames.ContentType] = MessagingNames.ContentTypeMessagePack,
                [KafkaEnvelopeHeaderNames.TenantId] = tenantId.ToString("D"),
                [KafkaEnvelopeHeaderNames.CorrelationId] = eventId.ToString("D"),
                [KafkaEnvelopeHeaderNames.TraceParent] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                [KafkaEnvelopeHeaderNames.Producer] = "fullnet.messaging",
                [KafkaEnvelopeHeaderNames.OccurredAtUtc] = occurredAt.ToString("O"),
            });

        var reader = new KafkaEnvelopeReader();
        var succeeded = reader.TryRead(consumeResult, out var envelope, out var failureCode);

        Assert.IsTrue(succeeded);
        Assert.IsNull(failureCode);
        Assert.IsNotNull(envelope);
        Assert.AreEqual(eventId, envelope!.EventId);
        Assert.AreEqual("tenant-42", envelope.PartitionKey);
        Assert.AreEqual("fullnet.messaging.kafka.test.event", envelope.MessageType);
        Assert.AreEqual(1, envelope.SchemaVersion);
        Assert.AreEqual(tenantId, envelope.TenantId);
        Assert.AreEqual("fullnet.messaging", envelope.Producer);
        CollectionAssert.AreEqual(SamplePayload, envelope.Payload.ToArray());
    }

    [TestMethod]
    public void TryRead_rejects_missing_event_id_header()
    {
        var consumeResult = CreateConsumeResult(
            partitionKey: "tenant-42",
            payload: SamplePayload,
            new Dictionary<string, string>
            {
                [KafkaEnvelopeHeaderNames.MessageType] = "fullnet.messaging.kafka.test.event",
                [KafkaEnvelopeHeaderNames.SchemaVersion] = "1",
                [KafkaEnvelopeHeaderNames.ContentType] = MessagingNames.ContentTypeMessagePack,
                [KafkaEnvelopeHeaderNames.Producer] = "fullnet.messaging",
                [KafkaEnvelopeHeaderNames.OccurredAtUtc] = DateTimeOffset.UtcNow.ToString("O"),
            });

        var reader = new KafkaEnvelopeReader();
        var succeeded = reader.TryRead(consumeResult, out _, out var failureCode);

        Assert.IsFalse(succeeded);
        Assert.AreEqual(
            IntegrationEventFailureCodes.ContractPrefix + "event_id_invalid",
            failureCode);
    }

    [TestMethod]
    public void TryRead_rejects_invalid_message_type()
    {
        var consumeResult = CreateConsumeResult(
            partitionKey: "tenant-42",
            payload: SamplePayload,
            CreateRequiredHeaders(messageType: "INVALID"));

        var reader = new KafkaEnvelopeReader();
        var succeeded = reader.TryRead(consumeResult, out _, out var failureCode);

        Assert.IsFalse(succeeded);
        Assert.AreEqual(IntegrationEventFailureCodes.MessageTypeInvalid, failureCode);
    }

    [TestMethod]
    public void TryRead_rejects_empty_payload()
    {
        var consumeResult = CreateConsumeResult(
            partitionKey: "tenant-42",
            payload: [],
            CreateRequiredHeaders());

        var reader = new KafkaEnvelopeReader();
        var succeeded = reader.TryRead(consumeResult, out _, out var failureCode);

        Assert.IsFalse(succeeded);
        Assert.AreEqual(IntegrationEventFailureCodes.PayloadRequired, failureCode);
    }

    private static Dictionary<string, string> CreateRequiredHeaders(
        string messageType = "fullnet.messaging.kafka.test.event") =>
        new()
        {
            [KafkaEnvelopeHeaderNames.EventId] = Guid.CreateVersion7().ToString("D"),
            [KafkaEnvelopeHeaderNames.MessageType] = messageType,
            [KafkaEnvelopeHeaderNames.SchemaVersion] = "1",
            [KafkaEnvelopeHeaderNames.ContentType] = MessagingNames.ContentTypeMessagePack,
            [KafkaEnvelopeHeaderNames.Producer] = "fullnet.messaging",
            [KafkaEnvelopeHeaderNames.OccurredAtUtc] = DateTimeOffset.UtcNow.ToString("O"),
        };

    private static ConsumeResult<string, byte[]> CreateConsumeResult(
        string partitionKey,
        byte[] payload,
        IReadOnlyDictionary<string, string> headers)
    {
        var messageHeaders = new Headers();
        foreach (var header in headers)
        {
            messageHeaders.Add(header.Key, Encoding.UTF8.GetBytes(header.Value));
        }

        return new ConsumeResult<string, byte[]>
        {
            Topic = "messaging.kafka-test.v1",
            Partition = 0,
            Offset = 17,
            Message = new Message<string, byte[]>
            {
                Key = partitionKey,
                Value = payload,
                Headers = messageHeaders,
            },
        };
    }
}