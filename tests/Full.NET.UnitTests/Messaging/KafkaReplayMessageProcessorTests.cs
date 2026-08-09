using System.Text;
using Confluent.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaReplayMessageProcessorTests
{
    private const string ConsumerName = "fullnet.test.replay-projection";
    private const string MessageType = "fullnet.test.replay.changed";

    [TestMethod]
    public async Task Replay_processor_reuses_dispatcher_inbox_and_generated_route()
    {
        var subscription = new RecordingSubscription();
        var catalog = new IntegrationEventSubscriptionCatalog(
            [IntegrationEventTopicDefinition.Create(
                "messaging.test-replay.v1",
                MessageType,
                1,
                EventDeliveryOwner.CdcKafka)],
            [subscription]);
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(
                ConsumerName,
                Arg.Any<IntegrationEventEnvelope>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new InboxClaimResult(InboxClaimStatus.Claimed),
                new InboxClaimResult(InboxClaimStatus.AlreadyProcessed));
        inbox.MarkProcessedAsync(
                ConsumerName,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var ownershipGate = Substitute.For<IEventStreamOwnershipGate>();
        ownershipGate.AcquireConsumerFenceAsync(
                MessageType,
                1,
                Arg.Any<CancellationToken>())
            .Returns(EventStreamConsumerFenceResult.Acquired(EventDeliveryOwner.CdcKafka));
        var dispatcher = new IntegrationEventConsumerDispatcher(
            new PassthroughTransaction(),
            inbox,
            catalog,
            ownershipGate,
            Substitute.For<IEffectiveEventDeliveryOwnerResolver>(),
            new CurrentTenantAccessor(),
            [new TestRegistry()]);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventSubscriptionCatalog>(catalog);
        services.AddSingleton(dispatcher);
        services.AddSingleton<IIntegrationEventHandlerRegistry>(new TestRegistry());
        using var provider = services.BuildServiceProvider();
        var processor = new KafkaReplayMessageProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new KafkaEnvelopeReader());
        var message = CreateMessage();

        var first = await processor.ProcessAsync(
            ConsumerName,
            message,
            CancellationToken.None);
        var duplicate = await processor.ProcessAsync(
            ConsumerName,
            message,
            CancellationToken.None);

        Assert.AreEqual(KafkaReplayMessageOutcome.Processed, first);
        Assert.AreEqual(KafkaReplayMessageOutcome.AlreadyProcessed, duplicate);
        Assert.AreEqual(1, subscription.HandledCount);
    }

    private static ConsumeResult<string, byte[]> CreateMessage()
    {
        var eventId = Guid.CreateVersion7();
        var headers = new Headers
        {
            { KafkaEnvelopeHeaderNames.EventId, Encoding.UTF8.GetBytes(eventId.ToString("D")) },
            { KafkaEnvelopeHeaderNames.MessageType, Encoding.UTF8.GetBytes(MessageType) },
            { KafkaEnvelopeHeaderNames.SchemaVersion, Encoding.UTF8.GetBytes("1") },
            { KafkaEnvelopeHeaderNames.ContentType, Encoding.UTF8.GetBytes(MessagingNames.ContentTypeMessagePack) },
            { KafkaEnvelopeHeaderNames.Producer, Encoding.UTF8.GetBytes("fullnet.tests") },
            { KafkaEnvelopeHeaderNames.OccurredAtUtc, Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) },
        };
        return new ConsumeResult<string, byte[]>
        {
            Topic = "messaging.test-replay.v1",
            Partition = 0,
            Offset = 10,
            Message = new Message<string, byte[]>
            {
                Key = "projection-1",
                Value = [0x01],
                Headers = headers,
            },
        };
    }

    private sealed class RecordingSubscription : IIntegrationEventSubscription
    {
        public string ConsumerName => KafkaReplayMessageProcessorTests.ConsumerName;

        public string EventType => MessageType;

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public int HandledCount { get; private set; }

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestRegistry : IIntegrationEventHandlerRegistry
    {
        public bool TryResolve(
            string messageType,
            int schemaVersion,
            string consumerName,
            out IntegrationEventHandlerDescriptor descriptor)
        {
            descriptor = new IntegrationEventHandlerDescriptor(
                MessageType,
                1,
                ConsumerName,
                typeof(RecordingSubscription));
            return string.Equals(messageType, MessageType, StringComparison.Ordinal)
                && schemaVersion == 1
                && string.Equals(consumerName, ConsumerName, StringComparison.Ordinal);
        }
    }

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
