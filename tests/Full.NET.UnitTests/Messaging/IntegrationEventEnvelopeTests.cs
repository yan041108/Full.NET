using Full.NET.Messaging.Abstractions;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class IntegrationEventEnvelopeTests
{
    private static readonly byte[] SamplePayload = [0x01, 0x02];

    [TestMethod]
    public void Create_accepts_valid_envelope()
    {
        var eventId = Guid.CreateVersion7();
        var envelope = IntegrationEventEnvelope.Create(
            eventId,
            "fullnet.tenancy.tenant.changed",
            1,
            MessagingNames.ContentTypeMessagePack,
            Guid.CreateVersion7(),
            "tenant-42",
            eventId.ToString(),
            null,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "fullnet.tenancy",
            DateTimeOffset.UtcNow,
            SamplePayload);

        Assert.AreEqual(eventId, envelope.EventId);
        Assert.AreEqual("tenant-42", envelope.PartitionKey);
        Assert.AreEqual("fullnet.tenancy", envelope.Producer);
    }

    [TestMethod]
    public void Create_rejects_empty_partition_key()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventEnvelope.Create(
                Guid.CreateVersion7(),
                "fullnet.tenancy.tenant.changed",
                1,
                MessagingNames.ContentTypeMessagePack,
                null,
                "",
                null,
                null,
                null,
                "fullnet.tenancy",
                DateTimeOffset.UtcNow,
                SamplePayload));
    }

    [TestMethod]
    public void Create_rejects_invalid_schema_version()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventEnvelope.Create(
                Guid.CreateVersion7(),
                "fullnet.tenancy.tenant.changed",
                0,
                MessagingNames.ContentTypeMessagePack,
                null,
                "tenant-42",
                null,
                null,
                null,
                "fullnet.tenancy",
                DateTimeOffset.UtcNow,
                SamplePayload));
    }

    [TestMethod]
    public void Metadata_create_rejects_overlong_producer()
    {
        var overlongProducer = new string('a', MessagingNames.ProducerMaxLength + 1);
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventMetadata.Create(
                "tenant-42",
                overlongProducer));
    }

    [TestMethod]
    public void Metadata_create_rejects_partition_key_exceeding_utf8_limit()
    {
        var overlongKey = new string('x', MessagingNames.PartitionKeyMaxUtf8Bytes + 1);
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventMetadata.Create(
                overlongKey,
                "fullnet.tenancy"));
    }

    [TestMethod]
    public void Create_rejects_invalid_trace_parent()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventEnvelope.Create(
                Guid.CreateVersion7(),
                "fullnet.tenancy.tenant.changed",
                1,
                MessagingNames.ContentTypeMessagePack,
                null,
                "tenant-42",
                null,
                null,
                "not-a-trace-parent",
                "fullnet.tenancy",
                DateTimeOffset.UtcNow,
                SamplePayload));
    }

    [TestMethod]
    public void Create_rejects_invalid_message_type()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventEnvelope.Create(
                Guid.CreateVersion7(),
                "invalid-message-type",
                1,
                MessagingNames.ContentTypeMessagePack,
                null,
                "tenant-42",
                null,
                null,
                null,
                "fullnet.tenancy",
                DateTimeOffset.UtcNow,
                SamplePayload));
    }
}