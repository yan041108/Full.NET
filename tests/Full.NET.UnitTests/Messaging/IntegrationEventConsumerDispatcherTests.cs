using System.Diagnostics;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class IntegrationEventConsumerDispatcherTests
{
    private const string ConsumerName = "fullnet.messaging.inbox.test";
    private const string EventType = "fullnet.messaging.inbox.test.event";
    private const string TopicCode = "messaging.inbox-test.v1";

    [TestMethod]
    public async Task ConsumeAsync_creates_inbox_transaction_activity()
    {
        Activity? stopped = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == IntegrationEventConsumerTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped = activity,
        };
        ActivitySource.AddActivityListener(listener);
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(
                Arg.Any<string>(),
                Arg.Any<IntegrationEventEnvelope>(),
                Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.AlreadyProcessed));
        var handler = new RecordingSubscription();

        await CreateDispatcher(inbox, handler).ConsumeAsync(
            ConsumerName,
            CreateEnvelope([0x01]),
            handler,
            CancellationToken.None);

        Assert.IsNotNull(stopped);
        Assert.AreEqual("fullnet.messaging.inbox.transaction", stopped.OperationName);
        Assert.AreEqual(ConsumerName, stopped.GetTagItem("messaging.consumer.group.name"));
    }

    [TestMethod]
    public async Task ConsumeAsync_processes_first_delivery()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.Claimed));
        inbox.MarkProcessedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = new RecordingSubscription();
        var dispatcher = CreateDispatcher(inbox, handler);

        var envelope = CreateEnvelope([1, 2, 3]);
        var result = await dispatcher.ConsumeAsync(
            ConsumerName,
            envelope,
            handler,
            CancellationToken.None);

        Assert.AreEqual(InboxConsumeStatus.Processed, result.Status);
        Assert.IsTrue(handler.Handled);
        await inbox.Received(1).MarkProcessedAsync(
            ConsumerName,
            envelope.EventId,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConsumeAsync_returns_already_processed_without_handler()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.AlreadyProcessed));

        var handler = new RecordingSubscription();
        var dispatcher = CreateDispatcher(inbox, handler);

        var envelope = CreateEnvelope([9]);
        var result = await dispatcher.ConsumeAsync(
            ConsumerName,
            envelope,
            handler,
            CancellationToken.None);

        Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, result.Status);
        Assert.IsFalse(handler.Handled);
        await inbox.DidNotReceive().MarkProcessedAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConsumeAsync_payload_mismatch_is_permanent_contract_failure()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.PayloadMismatch));

        var handler = new RecordingSubscription();
        var dispatcher = CreateDispatcher(inbox, handler);

        var exception = await Assert.ThrowsExactlyAsync<IntegrationEventPermanentException>(() =>
            dispatcher.ConsumeAsync(
                ConsumerName,
                CreateEnvelope([7]),
                handler,
                CancellationToken.None));

        Assert.AreEqual(
            IntegrationEventFailureCodes.MessageIdPayloadMismatch,
            exception.Failure.Code);
        Assert.IsFalse(handler.Handled);
    }

    [TestMethod]
    public async Task ConsumeAsync_restores_host_scope_for_host_envelope()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.Claimed));
        inbox.MarkProcessedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var currentTenant = new CurrentTenantAccessor();
        var handler = new TenantObservingSubscription(currentTenant);
        var dispatcher = CreateDispatcher(inbox, handler, currentTenant);

        await dispatcher.ConsumeAsync(
            ConsumerName,
            CreateEnvelope([1], tenantId: null),
            handler,
            CancellationToken.None);

        Assert.IsTrue(handler.ObservedHost);
        Assert.IsFalse(currentTenant.IsAvailable);
    }

    [TestMethod]
    public async Task ConsumeAsync_restores_tenant_scope_from_envelope()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.Claimed));
        inbox.MarkProcessedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var tenantId = Guid.CreateVersion7();
        var currentTenant = new CurrentTenantAccessor();
        var handler = new TenantObservingSubscription(currentTenant);
        var dispatcher = CreateDispatcher(inbox, handler, currentTenant);

        await dispatcher.ConsumeAsync(
            ConsumerName,
            CreateEnvelope([1], tenantId: tenantId),
            handler,
            CancellationToken.None);

        Assert.AreEqual(tenantId, handler.ObservedTenantId);
        Assert.IsFalse(currentTenant.IsAvailable);
    }

    [TestMethod]
    public async Task ConsumeAsync_keeps_tenant_scope_until_async_handler_completes()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.Claimed));
        inbox.MarkProcessedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var tenantId = Guid.CreateVersion7();
        var currentTenant = new CurrentTenantAccessor();
        var handler = new AsyncTenantObservingSubscription();
        var dispatcher = CreateDispatcher(inbox, handler, currentTenant);

        var consumeTask = dispatcher.ConsumeAsync(
            ConsumerName,
            CreateEnvelope([1], tenantId: tenantId),
            handler,
            CancellationToken.None);

        await handler.Started.Task;
        Assert.AreEqual(tenantId, currentTenant.Id);
        handler.Release.TrySetResult();
        await consumeTask;
        Assert.IsFalse(currentTenant.IsAvailable);
    }

    [TestMethod]
    public async Task ConsumeAsync_handler_exception_propagates()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.Claimed));

        var handler = new ThrowingSubscription();
        var dispatcher = CreateDispatcher(inbox, handler);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            dispatcher.ConsumeAsync(
                ConsumerName,
                CreateEnvelope([5]),
                handler,
                CancellationToken.None));

        await inbox.DidNotReceive().MarkProcessedAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConsumeAsync_rejects_unregistered_handler_instance()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        var registered = new RecordingSubscription();
        var other = new RecordingSubscription();
        var dispatcher = CreateDispatcher(inbox, registered);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            dispatcher.ConsumeAsync(
                ConsumerName,
                CreateEnvelope([1]),
                other,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ConsumeAsync_rejects_delivery_after_stream_ownership_is_revoked()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        var handler = new RecordingSubscription();
        var dispatcher = CreateDispatcher(
            inbox,
            handler,
            deliveryOwner: EventDeliveryOwner.LegacyPolling);

        await Assert.ThrowsExactlyAsync<EventDeliveryOwnershipRevokedException>(() =>
            dispatcher.ConsumeAsync(
                ConsumerName,
                CreateEnvelope([1]),
                handler,
                CancellationToken.None));

        await inbox.DidNotReceive().ClaimAsync(
            Arg.Any<string>(),
            Arg.Any<IntegrationEventEnvelope>(),
            Arg.Any<CancellationToken>());
        Assert.IsFalse(handler.Handled);
    }

    [TestMethod]
    public async Task ConsumeAsync_uses_atomic_consumer_fence_without_second_owner_query()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(Arg.Any<string>(), Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.AlreadyProcessed));
        var handler = new RecordingSubscription();
        var catalog = new IntegrationEventSubscriptionCatalog(
            [
                IntegrationEventTopicDefinition.Create(
                    TopicCode,
                    EventType,
                    1,
                    EventDeliveryOwner.CdcKafka),
            ],
            [handler]);
        var ownershipGate = Substitute.For<IEventStreamOwnershipGate>();
        ownershipGate.AcquireConsumerFenceAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(EventStreamConsumerFenceResult.Acquired(EventDeliveryOwner.CdcKafka));
        var ownerResolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
        var dispatcher = new IntegrationEventConsumerDispatcher(
            new PassthroughTransaction(),
            inbox,
            catalog,
            ownershipGate,
            ownerResolver,
            new CurrentTenantAccessor());

        var result = await dispatcher.ConsumeAsync(
            ConsumerName,
            CreateEnvelope([1]),
            handler,
            CancellationToken.None);

        Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, result.Status);
        await ownershipGate.DidNotReceive().AcquireConsumerAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await ownerResolver.DidNotReceive().GetDeliveryOwnerAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConsumeAsync_prefers_generated_registry_and_keeps_catalog_as_fallback()
    {
        var inbox = Substitute.For<IIntegrationEventInbox>();
        inbox.ClaimAsync(
                Arg.Any<string>(),
                Arg.Any<IntegrationEventEnvelope>(),
                Arg.Any<CancellationToken>())
            .Returns(new InboxClaimResult(InboxClaimStatus.AlreadyProcessed));
        var handler = new RecordingSubscription();
        var catalog = new IntegrationEventSubscriptionCatalog(
            [
                IntegrationEventTopicDefinition.Create(
                    TopicCode,
                    EventType,
                    1,
                    EventDeliveryOwner.CdcKafka),
            ],
            [handler]);
        var ownershipGate = Substitute.For<IEventStreamOwnershipGate>();
        ownershipGate.AcquireConsumerAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var ownerResolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
        ownerResolver.GetDeliveryOwnerAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(EventDeliveryOwner.CdcKafka);
        var dispatcher = new IntegrationEventConsumerDispatcher(
            new PassthroughTransaction(),
            inbox,
            catalog,
            ownershipGate,
            ownerResolver,
            new CurrentTenantAccessor(),
            [new TestGeneratedRegistry(typeof(RecordingSubscription))]);

        var result = await dispatcher.ConsumeAsync(
            ConsumerName,
            CreateEnvelope([1]),
            handler,
            CancellationToken.None);

        Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, result.Status);
    }

    private static IntegrationEventConsumerDispatcher CreateDispatcher(
        IIntegrationEventInbox inbox,
        IIntegrationEventSubscription subscription,
        CurrentTenantAccessor? currentTenant = null,
        EventDeliveryOwner deliveryOwner = EventDeliveryOwner.CdcKafka)
    {
        currentTenant ??= new CurrentTenantAccessor();
        var catalog = new IntegrationEventSubscriptionCatalog(
            [
                IntegrationEventTopicDefinition.Create(
                    TopicCode,
                    EventType,
                    1,
                    EventDeliveryOwner.CdcKafka),
            ],
            [subscription]);
        var ownershipGate = Substitute.For<IEventStreamOwnershipGate>();
        ownershipGate.AcquireConsumerAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var ownerResolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
        ownerResolver.GetDeliveryOwnerAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(deliveryOwner);

        return new IntegrationEventConsumerDispatcher(
            new PassthroughTransaction(),
            inbox,
            catalog,
            ownershipGate,
            ownerResolver,
            currentTenant);
    }

    private static IntegrationEventEnvelope CreateEnvelope(
        byte[] payload,
        Guid? eventId = null,
        Guid? tenantId = null) =>
        IntegrationEventEnvelope.Create(
            eventId ?? Guid.CreateVersion7(),
            EventType,
            1,
            MessagingNames.ContentTypeMemoryPack,
            tenantId,
            Guid.CreateVersion7().ToString("D"),
            "inbox-unit-test",
            null,
            null,
            "fullnet.messaging.tests",
            DateTimeOffset.UtcNow,
            payload);

    private sealed class RecordingSubscription : IIntegrationEventSubscription
    {
        public string ConsumerName => ConsumerNameValue;
        public string EventType => EventTypeValue;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
        public bool Handled { get; private set; }

        private const string ConsumerNameValue = IntegrationEventConsumerDispatcherTests.ConsumerName;
        private const string EventTypeValue = IntegrationEventConsumerDispatcherTests.EventType;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestGeneratedRegistry(Type handlerType)
        : IIntegrationEventHandlerRegistry
    {
        public bool TryResolve(
            string messageType,
            int schemaVersion,
            string consumerName,
            out IntegrationEventHandlerDescriptor descriptor)
        {
            descriptor = new IntegrationEventHandlerDescriptor(
                messageType,
                schemaVersion,
                consumerName,
                handlerType);
            return true;
        }
    }

    private sealed class ThrowingSubscription : IIntegrationEventSubscription
    {
        public string ConsumerName => ConsumerNameValue;
        public string EventType => EventTypeValue;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        private const string ConsumerNameValue = IntegrationEventConsumerDispatcherTests.ConsumerName;
        private const string EventTypeValue = IntegrationEventConsumerDispatcherTests.EventType;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("handler failed");
    }

    private sealed class TenantObservingSubscription(CurrentTenantAccessor currentTenant)
        : IIntegrationEventSubscription
    {
        public string ConsumerName => ConsumerNameValue;
        public string EventType => EventTypeValue;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
        public bool ObservedHost { get; private set; }
        public Guid? ObservedTenantId { get; private set; }

        private const string ConsumerNameValue = IntegrationEventConsumerDispatcherTests.ConsumerName;
        private const string EventTypeValue = IntegrationEventConsumerDispatcherTests.EventType;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            ObservedHost = currentTenant.IsHost;
            ObservedTenantId = currentTenant.Id;
            return Task.CompletedTask;
        }
    }

    private sealed class AsyncTenantObservingSubscription
        : IIntegrationEventSubscription
    {
        public string ConsumerName => ConsumerNameValue;
        public string EventType => EventTypeValue;
        public int SchemaVersion => 1;
        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private const string ConsumerNameValue = IntegrationEventConsumerDispatcherTests.ConsumerName;
        private const string EventTypeValue = IntegrationEventConsumerDispatcherTests.EventType;

        public async Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            await action(cancellationToken).ConfigureAwait(false);
    }
}
