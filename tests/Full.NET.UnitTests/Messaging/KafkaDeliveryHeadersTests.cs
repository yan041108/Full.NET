using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaDeliveryHeadersTests
{
    [TestMethod]
    public void Mutable_delivery_headers_replace_previous_retry_metadata()
    {
        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "fullnet.test.events.v1.retry.5s",
            Partition = 0,
            Offset = 17,
            Message = new Message<string, byte[]>
            {
                Key = "aggregate-1",
                Value = [0x01],
                Headers = new Headers(),
            },
        };
        var failure = new IntegrationEventFailure(
            IntegrationEventFailureKind.Transient,
            IntegrationEventFailureCodes.TransientPrefix + "database",
            "Transient database failure.");
        var firstNotBefore = DateTimeOffset.UtcNow.AddSeconds(5);
        var secondNotBefore = firstNotBefore.AddMinutes(1);

        KafkaDeliveryHeaders.ApplyFailureMetadata(
            consumeResult.Message.Headers,
            "fullnet.test.consumer",
            consumeResult,
            failure,
            attemptCount: 1,
            DateTimeOffset.UtcNow);
        KafkaDeliveryHeaders.SetRetryNotBeforeUtc(
            consumeResult.Message.Headers,
            firstNotBefore);
        KafkaDeliveryHeaders.ApplyFailureMetadata(
            consumeResult.Message.Headers,
            "fullnet.test.consumer",
            consumeResult,
            failure,
            attemptCount: 2,
            DateTimeOffset.UtcNow);
        KafkaDeliveryHeaders.SetRetryNotBeforeUtc(
            consumeResult.Message.Headers,
            secondNotBefore);

        Assert.AreEqual(2, KafkaDeliveryHeaders.ReadAttemptCount(consumeResult.Message.Headers));
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadRetryNotBeforeUtc(
            consumeResult.Message.Headers,
            out var actualNotBefore));
        Assert.AreEqual(secondNotBefore, actualNotBefore);
        Assert.AreEqual(
            1,
            consumeResult.Message.Headers.Count(
                header => header.Key == KafkaDeliveryHeaderNames.AttemptCount));
        Assert.AreEqual(
            1,
            consumeResult.Message.Headers.Count(
                header => header.Key == KafkaDeliveryHeaderNames.RetryNotBeforeUtc));
    }

    [TestMethod]
    public void Retry_hops_preserve_original_source_position_for_dead_letter_traceability()
    {
        var headers = new Headers();
        var failure = new IntegrationEventFailure(
            IntegrationEventFailureKind.Transient,
            IntegrationEventFailureCodes.TransientPrefix + "database",
            "Transient database failure.");
        var original = CreateConsumeResult(
            "fullnet.test.events.v1",
            partition: 3,
            offset: 41,
            headers);
        KafkaDeliveryHeaders.ApplyFailureMetadata(
            headers,
            "fullnet.test.consumer",
            original,
            failure,
            attemptCount: 1,
            DateTimeOffset.UtcNow);

        var retry = CreateConsumeResult(
            "fullnet.test.events.v1.retry.5s",
            partition: 1,
            offset: 9,
            headers);
        KafkaDeliveryHeaders.ApplyFailureMetadata(
            headers,
            "fullnet.test.consumer",
            retry,
            failure,
            attemptCount: 2,
            DateTimeOffset.UtcNow);

        Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
            headers,
            KafkaDeliveryHeaderNames.SourceTopic,
            out var sourceTopic));
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
            headers,
            KafkaDeliveryHeaderNames.SourcePartition,
            out var sourcePartition));
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
            headers,
            KafkaDeliveryHeaderNames.SourceOffset,
            out var sourceOffset));
        Assert.AreEqual("fullnet.test.events.v1", sourceTopic);
        Assert.AreEqual("3", sourcePartition);
        Assert.AreEqual("41", sourceOffset);
    }

    private static ConsumeResult<string, byte[]> CreateConsumeResult(
        string topic,
        int partition,
        long offset,
        Headers headers) =>
        new()
        {
            Topic = topic,
            Partition = partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = "aggregate-1",
                Value = [0x01],
                Headers = headers,
            },
        };
}
