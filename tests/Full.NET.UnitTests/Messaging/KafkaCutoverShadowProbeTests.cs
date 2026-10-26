using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>有界完整扫描必须区分合法证明、信封失配及扫描窗口变化。</summary>
[TestClass]
public sealed class KafkaCutoverShadowProbeTests
{
    [TestMethod]
    public async Task Complete_shadow_snapshot_can_be_compared_without_business_processing()
    {
        await using var fixture = new ProbeFixture();
        var digest = await fixture.Probe.CompareShadowAsync(ProbeFixture.EventType, 1, fixture.Expected);
        Assert.IsNotNull(digest);
        Assert.AreEqual(64, digest.Length);
        fixture.Consumer.Received(1).Assign(Arg.Is<IReadOnlyList<TopicPartitionOffset>>(item =>
            item != null && item.Count == 1 && item[0].Offset.Value == 0));
    }

    [TestMethod]
    [DataRow("previous_generation")]
    [DataRow("identical_copy")]
    [DataRow("old_history")]
    public async Task Reused_topic_and_identical_at_least_once_copies_preserve_complete_generation(string mode)
    {
        await using var fixture = new ProbeFixture(mode);
        Assert.IsNotNull(await fixture.Probe.CompareShadowAsync(ProbeFixture.EventType, 1, fixture.Expected));
        if (mode == "old_history") fixture.Consumer.Received(1).Assign(Arg.Is<IReadOnlyList<TopicPartitionOffset>>(items =>
            items != null && items.Single().Offset.Value == 99999));
    }

    [TestMethod]
    [DataRow("generation_missing")]
    [DataRow("generation_wrong")]
    [DataRow("payload")]
    [DataRow("tenant")]
    [DataRow("producer")]
    [DataRow("correlation")]
    [DataRow("causation")]
    [DataRow("trace")]
    [DataRow("timestamp")]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("topology")]
    [DataRow("retention")]
    public async Task Changed_or_incomplete_shadow_input_cannot_create_evidence(string fault)
    {
        await using var fixture = new ProbeFixture(fault);
        Assert.IsNull(await fixture.Probe.CompareShadowAsync(ProbeFixture.EventType, 1, fixture.Expected));
    }

    private sealed class ProbeFixture : IAsyncDisposable
    {
        internal const string EventType = "fullnet.messaging.order.changed";
        private const string Shadow = "fullnet.shadow.orders.v1";
        private readonly HttpClient _http = new() { BaseAddress = new Uri("http://connect:8083") };
        internal IKafkaReplayConsumer Consumer { get; } = Substitute.For<IKafkaReplayConsumer>();
        internal KafkaEventDeliveryCutoverProbe Probe { get; }
        internal CutoverShadowEvent[] Expected { get; }

        internal ProbeFixture(string? fault = null)
        {
            var now = DateTimeOffset.UtcNow.AddSeconds(-1);
            var generation = Guid.CreateVersion7();
            Expected = [Sample("order-a", [1]), Sample("order-b", [2])];
            CutoverShadowEvent Sample(string key, byte[] payload) => new(Guid.CreateVersion7(), key, payload, now,
                "application/x-memorypack", null, null, null, null, "fullnet.messaging.tests", generation);
            var partition = new TopicPartition(Shadow, 0);
            Consumer.GetPartitions(Shadow, Arg.Any<TimeSpan>()).Returns(
                [partition], fault == "topology" ? [partition, new(Shadow, 1)] : [partition]);
            Consumer.QueryWatermarkOffsets(partition, Arg.Any<TimeSpan>()).Returns(
                new WatermarkOffsets(0, fault == "old_history" ? 100001 : fault is "previous_generation" or "identical_copy" ? 3 : 2),
                fault == "retention" ? new WatermarkOffsets(1, 2) : new WatermarkOffsets(0, fault == "old_history" ? 100001 : fault is "previous_generation" or "identical_copy" ? 3 : 2));
            Consumer.OffsetsForTimes(Arg.Any<IReadOnlyList<TopicPartitionTimestamp>>(), Arg.Any<TimeSpan>()).Returns([new(partition, fault == "old_history" ? 99999 : 0)]);
            var first = Record(Expected[0], 0);
            var second = Record(fault == "duplicate" ? Expected[0] : Expected[1], 1);
            switch (fault)
            {
                case "generation_missing": second.Message.Headers.Remove("capture_generation"); break;
                case "generation_wrong": second.Message.Headers.Remove("capture_generation"); second.Message.Headers.Add("capture_generation", Encoding.UTF8.GetBytes(Guid.CreateVersion7().ToString("D"))); break;
                case "payload": second.Message.Value = [99]; break;
                case "tenant": second.Message.Headers.Add(KafkaEnvelopeHeaderNames.TenantId, Encoding.UTF8.GetBytes(Guid.CreateVersion7().ToString("D"))); break;
                case "producer": second.Message.Headers.Remove(KafkaEnvelopeHeaderNames.Producer); second.Message.Headers.Add(KafkaEnvelopeHeaderNames.Producer, "fullnet.messaging.other"u8.ToArray()); break;
                case "correlation": second.Message.Headers.Add(KafkaEnvelopeHeaderNames.CorrelationId, "unexpected-correlation"u8.ToArray()); break;
                case "causation": second.Message.Headers.Add(KafkaEnvelopeHeaderNames.CausationId, Encoding.UTF8.GetBytes(Guid.CreateVersion7().ToString("D"))); break;
                case "trace": second.Message.Headers.Add(KafkaEnvelopeHeaderNames.TraceParent, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"u8.ToArray()); break;
                case "timestamp": second.Message.Headers.Remove(KafkaEnvelopeHeaderNames.OccurredAtUtc); second.Message.Headers.Add(KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(now.AddSeconds(1).ToString("O"))); break;
                case "missing": second = new() { Topic = Shadow, Partition = 0, Offset = 2, IsPartitionEOF = true }; break;
            }
            if (fault == "previous_generation") Consumer.Consume(Arg.Any<TimeSpan>()).Returns(
                Record(Expected[0] with { CaptureGeneration = Guid.CreateVersion7() }, 0), Record(Expected[0], 1), Record(Expected[1], 2));
            else if (fault == "identical_copy") Consumer.Consume(Arg.Any<TimeSpan>()).Returns(first, Record(Expected[0], 1), Record(Expected[1], 2));
            else if (fault == "old_history") Consumer.Consume(Arg.Any<TimeSpan>()).Returns(Record(Expected[0], 99999), Record(Expected[1], 100000));
            else Consumer.Consume(Arg.Any<TimeSpan>()).Returns(first, second);
            var factory = Substitute.For<IKafkaReplayConsumerFactory>();
            factory.Create(Arg.Any<ConsumerConfig>()).Returns(Consumer);
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns("Testing");
            Probe = new(Options.Create(new KafkaConnectRollbackOptions
            {
                Enabled = true, ConnectBaseUri = "http://connect:8083", Streams = [new()
                {
                    EventType = EventType, SchemaVersion = 1, TopicName = "messaging.orders.v1", ShadowTopicName = Shadow,
                    ConnectorName = "orders", ConsumerGroupId = "fullnet.messaging.orders",
                }],
            }), Options.Create(new KafkaMessagingOptions { Enabled = true }), environment,
                new IntegrationEventSubscriptionCatalog([IntegrationEventTopicDefinition.Create("messaging.orders.v1", EventType, 1, EventDeliveryOwner.LegacyPolling)], []),
                new KafkaConnectAdminClient(_http), factory);
        }

        private static ConsumeResult<string, byte[]> Record(CutoverShadowEvent sample, long offset) => new()
        {
            Topic = Shadow, Partition = 0, Offset = offset,
            Message = new()
            {
                Key = sample.PartitionKey, Value = sample.Payload,
                Headers =
                [
                    new("capture_generation", Encoding.UTF8.GetBytes(sample.CaptureGeneration.ToString("D"))),
                    new(KafkaEnvelopeHeaderNames.EventId, Encoding.UTF8.GetBytes(sample.Id.ToString("D"))),
                    new(KafkaEnvelopeHeaderNames.MessageType, Encoding.UTF8.GetBytes(EventType)),
                    new(KafkaEnvelopeHeaderNames.SchemaVersion, "1"u8.ToArray()),
                    new(KafkaEnvelopeHeaderNames.ContentType, "application/x-memorypack"u8.ToArray()),
                    new(KafkaEnvelopeHeaderNames.Producer, "fullnet.messaging.tests"u8.ToArray()),
                    new(KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(sample.OccurredAtUtc.ToString("O"))),
                ],
            },
        };

        public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }
    }
}
