using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class KafkaReplayTests
{
    [TestMethod]
    public async Task Range_replay_reads_fixed_offsets_without_advancing_formal_group_watermark()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.replay.g{Guid.NewGuid():N}.v1";
        var formalGroup = $"fullnet.test.replay.formal.g{Guid.NewGuid():N}";
        await environment.EnsureTopicsAsync(topic).ConfigureAwait(false);
        using (var producer = environment.CreateProducer("fullnet.kafka.replay.producer"))
        {
            for (var index = 0; index < 3; index++)
            {
                await producer.ProduceAsync(
                        topic,
                        KafkaTestMessages.Create(topic, "replay-key", [(byte)index]))
                    .ConfigureAwait(false);
            }

            producer.Flush(TimeSpan.FromSeconds(10));
        }

        TopicPartition partition;
        using (var formalConsumer = environment.CreateConsumer(
                   formalGroup,
                   "fullnet.kafka.replay.formal-first"))
        {
            formalConsumer.Subscribe(topic);
            var first = await KafkaTestMessages.ConsumeOneAsync(
                    formalConsumer,
                    TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
            partition = first.TopicPartition;
            formalConsumer.Commit(first);
            formalConsumer.Close();
        }

        var processor = new RecordingReplayProcessor();
        var options = environment.CreateOptions("fullnet.kafka.replay.range");
        var catalog = new IntegrationEventSubscriptionCatalog(
            [IntegrationEventTopicDefinition.Create(
                topic,
                "fullnet.messaging.kafka.test.event",
                1,
                EventDeliveryOwner.CdcKafka)],
            []);
        var service = new KafkaReplayService(
            catalog,
            new KafkaReplayConsumerFactory(),
            processor,
            Options.Create(options));
        var result = await service.ReplayAsync(
                new KafkaReplayRequest(
                    topic,
                    null,
                    null,
                    1,
                    2,
                    [partition.Partition.Value],
                    formalGroup,
                    10,
                    "integration range replay"),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.AreEqual(2, result.ProcessedMessages);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, processor.Offsets.ToArray());
        using var watermarkReader = environment.CreateConsumer(
            formalGroup,
            "fullnet.kafka.replay.formal-watermark");
        var committed = watermarkReader.Committed([partition], TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, committed.Single().Offset.Value);
    }

    private sealed class RecordingReplayProcessor : IKafkaReplayMessageProcessor
    {
        public List<long> Offsets { get; } = [];

        public Task<KafkaReplayMessageOutcome> ProcessAsync(
            string consumerName,
            ConsumeResult<string, byte[]> consumeResult,
            CancellationToken cancellationToken)
        {
            Offsets.Add(consumeResult.Offset.Value);
            return Task.FromResult(KafkaReplayMessageOutcome.Processed);
        }
    }
}
