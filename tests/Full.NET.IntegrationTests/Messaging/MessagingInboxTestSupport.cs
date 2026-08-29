using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Full.NET.Serialization.MemoryPack;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Messaging;

internal static class MessagingInboxTestSupport
{
    internal const string ConsumerName = "fullnet.messaging.inbox.test";
    internal const string DownstreamEventType = "fullnet.messaging.inbox.test.downstream";
    internal const string TopicCode = "messaging.inbox-test.v1";

    internal static IntegrationEventSubscriptionCatalog CreateCatalog(
        IIntegrationEventSubscription subscription) =>
        new(
            [
                IntegrationEventTopicDefinition.Create(
                    TopicCode,
                    MessagingOutboxTestSupport.TestEventType,
                    MessagingOutboxTestSupport.TestSchemaVersion,
                    EventDeliveryOwner.CdcKafka),
                IntegrationEventTopicDefinition.Create(
                    "messaging.inbox-downstream.v1",
                    DownstreamEventType,
                    MessagingOutboxTestSupport.TestSchemaVersion,
                    EventDeliveryOwner.CdcKafka),
            ],
            [subscription]);

    internal static ServiceProvider BuildInboxServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IEffectiveEventDeliveryOwnerResolver>(new CdcOwnerResolver());
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMemoryPack();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    internal static IntegrationEventConsumerDispatcher CreateDispatcher(
        IServiceScope scope,
        IIntegrationEventSubscription subscription) =>
        new(
            scope.ServiceProvider.GetRequiredService<ICommandTransaction>(),
            scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>(),
            CreateCatalog(subscription),
            new CdcOwnershipGate(),
            new CdcOwnerResolver(),
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>());

    internal static IntegrationEventEnvelope CreateEnvelope(
        byte[] payload,
        Guid eventId,
        Guid? tenantId = null,
        string partitionKey = "inbox-test-partition") =>
        IntegrationEventEnvelope.Create(
            eventId,
            MessagingOutboxTestSupport.TestEventType,
            MessagingOutboxTestSupport.TestSchemaVersion,
            MessagingNames.ContentTypeMemoryPack,
            tenantId,
            partitionKey,
            "messaging-inbox-test",
            null,
            null,
            "fullnet.messaging.tests",
            DateTimeOffset.UtcNow,
            payload);

    internal static async Task AssertPrecheckDoesNotOwnClaimAsync(
        IConfiguration configuration)
    {
        var eventId = Guid.CreateVersion7();
        var envelope = CreateEnvelope([0x71, 0x72], eventId);
        await using var services = BuildInboxServices(configuration);

        await using var precheckScope = services.CreateAsyncScope();
        var precheckInbox = precheckScope.ServiceProvider
            .GetRequiredService<IIntegrationEventInbox>();
        var precheck = await precheckInbox.PrecheckBatchAsync(
            ConsumerName,
            [new InboxMessageFingerprint(eventId, SHA256.HashData(envelope.Payload.Span))],
            CancellationToken.None);
        Assert.AreEqual(InboxPrecheckStatus.Unknown, precheck.Single().Status);

        // 模拟预检之后、正式 Claim 之前由另一消费者先提交；预检结果不得充当锁或所有权。
        await using (var competingScope = services.CreateAsyncScope())
        {
            var competingSubscription = new NoOpSubscription();
            var competingResult = await CreateDispatcher(
                    competingScope,
                    competingSubscription)
                .ConsumeAsync(
                    ConsumerName,
                    envelope,
                    competingSubscription,
                    CancellationToken.None);
            Assert.AreEqual(InboxConsumeStatus.Processed, competingResult.Status);
        }

        var originalSubscription = new NoOpSubscription();
        var originalResult = await CreateDispatcher(
                precheckScope,
                originalSubscription)
            .ConsumeAsync(
                ConsumerName,
                envelope,
                originalSubscription,
                CancellationToken.None);
        Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, originalResult.Status);
        Assert.IsFalse(originalSubscription.Handled);
    }

    private sealed class CdcOwnershipGate : IEventStreamOwnershipGate
    {
        public Task<bool> AcquireProducerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireConsumerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireOwnershipChangeAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class CdcOwnerResolver : IEffectiveEventDeliveryOwnerResolver
    {
        public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EventDeliveryOwner.CdcKafka);
    }

    internal sealed class DownstreamOutboxSubscription : IIntegrationEventSubscription
    {
        private readonly IOutboxWriter _outboxWriter;
        private readonly string _downstreamPartitionKey;

        internal DownstreamOutboxSubscription(
            IOutboxWriter outboxWriter,
            string downstreamPartitionKey)
        {
            _outboxWriter = outboxWriter;
            _downstreamPartitionKey = downstreamPartitionKey;
        }

        public string ConsumerName => MessagingInboxTestSupport.ConsumerName;

        public string EventType => MessagingOutboxTestSupport.TestEventType;

        public int SchemaVersion => MessagingOutboxTestSupport.TestSchemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public async Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            var downstreamPayload = new MessagingOutboxTestSupport.MessagingOutboxTestPayload(
                Convert.ToHexString(payload.Span));
            var metadata = IntegrationEventMetadata.Create(
                _downstreamPartitionKey,
                "fullnet.messaging.tests");
            await _outboxWriter.AddAsync(
                DownstreamEventType,
                MessagingOutboxTestSupport.TestSchemaVersion,
                downstreamPayload,
                metadata,
                cancellationToken);
        }
    }

    internal sealed class ThrowingSubscription : IIntegrationEventSubscription
    {
        public string ConsumerName => MessagingInboxTestSupport.ConsumerName;

        public string EventType => MessagingOutboxTestSupport.TestEventType;

        public int SchemaVersion => MessagingOutboxTestSupport.TestSchemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected inbox handler failure.");
    }

    internal sealed class NoOpSubscription : IIntegrationEventSubscription
    {
        public bool Handled { get; private set; }

        public string ConsumerName => MessagingInboxTestSupport.ConsumerName;

        public string EventType => MessagingOutboxTestSupport.TestEventType;

        public int SchemaVersion => MessagingOutboxTestSupport.TestSchemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }
}
