using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaGeneratedRouteResolutionTests
{
    [TestMethod]
    public void Worker_uses_generated_handler_type_as_the_primary_route()
    {
        const string consumerName = "fullnet.test.projection";
        const string messageType = "fullnet.test.changed";
        var subscription = new TestSubscription(consumerName, messageType);
        var catalog = Substitute.For<IIntegrationEventSubscriptionCatalog>();
        catalog.GetByHandlerTypeRequired(typeof(TestSubscription)).Returns(subscription);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandlerRegistry>(
            new TestRegistry(consumerName, messageType));
        using var provider = services.BuildServiceProvider();

        var resolved = KafkaConsumerWorker.ResolveSubscription(
            provider,
            catalog,
            consumerName,
            messageType,
            1);

        Assert.AreSame(subscription, resolved);
        catalog.Received(1).GetByHandlerTypeRequired(typeof(TestSubscription));
        catalog.DidNotReceiveWithAnyArgs().GetRequired(default!, default!, default);
    }

    private sealed class TestRegistry(string consumerName, string messageType)
        : IIntegrationEventHandlerRegistry
    {
        public bool TryResolve(
            string candidateMessageType,
            int schemaVersion,
            string candidateConsumerName,
            out IntegrationEventHandlerDescriptor descriptor)
        {
            var matched = string.Equals(candidateMessageType, messageType, StringComparison.Ordinal)
                && schemaVersion == 1
                && string.Equals(candidateConsumerName, consumerName, StringComparison.Ordinal);
            descriptor = matched
                ? new IntegrationEventHandlerDescriptor(
                    messageType,
                    1,
                    consumerName,
                    typeof(TestSubscription))
                : default;
            return matched;
        }
    }

    private sealed class TestSubscription(string consumerName, string eventType)
        : IIntegrationEventSubscription
    {
        public string ConsumerName { get; } = consumerName;

        public string EventType { get; } = eventType;

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
