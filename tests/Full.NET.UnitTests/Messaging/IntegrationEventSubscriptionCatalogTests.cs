using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class IntegrationEventSubscriptionCatalogTests
{
    private const string EventType = "fullnet.tenancy.tenant.changed";
    private const string TopicCode = "tenancy.tenant-changed.v1";

    [TestMethod]
    public void Create_allows_same_event_with_different_consumer_names()
    {
        var topic = CreateTopic(EventDeliveryOwner.LegacyPolling);
        var first = new TestSubscription("fullnet.tenancy.projector-a", EventType, 1);
        var second = new TestSubscription("fullnet.tenancy.projector-b", EventType, 1);

        var catalog = new IntegrationEventSubscriptionCatalog(
            [topic],
            [first, second]);

        Assert.AreSame(first, catalog.GetRequired(first.ConsumerName, EventType, 1));
        Assert.AreSame(second, catalog.GetRequired(second.ConsumerName, EventType, 1));
        Assert.AreEqual(EventDeliveryOwner.LegacyPolling, catalog.GetDeliveryOwner(EventType, 1));
    }

    [TestMethod]
    public void Generated_handler_type_resolves_the_same_scoped_subscription_instance()
    {
        var subscription = new TestSubscription(
            "fullnet.tenancy.projector-a",
            EventType,
            1);
        var catalog = new IntegrationEventSubscriptionCatalog(
            [CreateTopic(EventDeliveryOwner.CdcKafka)],
            [subscription]);

        Assert.AreSame(
            subscription,
            catalog.GetByHandlerTypeRequired(typeof(TestSubscription)));
    }

    [TestMethod]
    public void ResolveDeliveryOwner_uses_persisted_owner_when_present()
    {
        var topic = CreateTopic(EventDeliveryOwner.LegacyPolling);
        var catalog = new IntegrationEventSubscriptionCatalog([topic], []);
        Assert.AreEqual(
            EventDeliveryOwner.CdcKafka,
            catalog.ResolveDeliveryOwner(EventType, 1, EventDeliveryOwner.CdcKafka));
        Assert.AreEqual(
            EventDeliveryOwner.LegacyPolling,
            catalog.ResolveDeliveryOwner(EventType, 1, null));
    }

    [TestMethod]
    public void GetTopicByCodeRequired_resolves_only_registered_topic()
    {
        var topic = CreateTopic(EventDeliveryOwner.CdcKafka);
        var catalog = new IntegrationEventSubscriptionCatalog([topic], []);

        Assert.AreSame(topic, catalog.GetTopicByCodeRequired(TopicCode));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            catalog.GetTopicByCodeRequired("tenancy.unknown.v1"));
    }

    [TestMethod]
    public void Create_rejects_duplicate_consumer_name_registration()
    {
        var topic = CreateTopic(EventDeliveryOwner.LegacyPolling);
        var consumers = new[]
        {
            new IntegrationEventConsumerDefinition("fullnet.tenancy.projector-a"),
            new IntegrationEventConsumerDefinition("fullnet.tenancy.projector-a"),
        };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new IntegrationEventSubscriptionCatalog(
                [topic],
                consumers,
                [new TestSubscription("fullnet.tenancy.projector-a", EventType, 1)]));

        StringAssert.Contains(exception.Message, "duplicate ConsumerName");
        StringAssert.Contains(exception.Message, "fullnet.tenancy.projector-a");
    }

    [TestMethod]
    public void Create_rejects_same_route_within_consumer_name()
    {
        var topic = CreateTopic(EventDeliveryOwner.LegacyPolling);
        var first = new TestSubscription("fullnet.tenancy.projector-a", EventType, 1);
        var second = new TestSubscription("fullnet.tenancy.projector-a", EventType, 1);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new IntegrationEventSubscriptionCatalog([topic], [first, second]));

        StringAssert.Contains(exception.Message, "fullnet.tenancy.projector-a");
        StringAssert.Contains(exception.Message, EventType);
    }

    [TestMethod]
    public void Create_rejects_duplicate_consumer_name_event_type_schema_tuple()
    {
        // 目标：Identity 模块注册后再注册一个相同 (ConsumerName, EventType, SchemaVersion)
        // 的不同实现类型，必须在 catalog 构造时 fail-fast，不能让两个 Handler 都跑。
        var topic = IntegrationEventTopicDefinition.Create(
            "organization.unit-changed.v1",
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            1,
            EventDeliveryOwner.LegacyPolling);
        var first = new TestSubscription(
            "fullnet.identity.organization-unit-projection",
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            1);
        var second = new TestSubscription(
            "fullnet.identity.organization-unit-projection",
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            1);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new IntegrationEventSubscriptionCatalog([topic], [first, second]));

        StringAssert.Contains(exception.Message, "fullnet.identity.organization-unit-projection");
        StringAssert.Contains(
            exception.Message,
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged);
    }

    [TestMethod]
    public void Create_rejects_unknown_schema_version()
    {
        var topic = CreateTopic(EventDeliveryOwner.LegacyPolling);
        var subscription = new TestSubscription(
            "fullnet.tenancy.projector-a",
            EventType,
            schemaVersion: 2);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new IntegrationEventSubscriptionCatalog([topic], [subscription]));

        StringAssert.Contains(exception.Message, "unknown schema");
        StringAssert.Contains(exception.Message, "version 2");
    }

    [TestMethod]
    public void Create_rejects_invalid_topic_code()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            IntegrationEventTopicDefinition.Create(
                "INVALID_TOPIC_CODE",
                EventType,
                1,
                EventDeliveryOwner.LegacyPolling));

        StringAssert.Contains(exception.Message, IntegrationEventFailureCodes.TopicCodeInvalid);
    }

    [TestMethod]
    public void Create_rejects_simultaneous_legacy_polling_and_cdc_kafka_ownership()
    {
        var legacyTopic = IntegrationEventTopicDefinition.Create(
            TopicCode,
            EventType,
            1,
            EventDeliveryOwner.LegacyPolling);
        var kafkaTopic = IntegrationEventTopicDefinition.Create(
            "tenancy.tenant-changed.kafka.v1",
            EventType,
            1,
            EventDeliveryOwner.CdcKafka);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new IntegrationEventSubscriptionCatalog(
                [legacyTopic, kafkaTopic],
                []));

        StringAssert.Contains(exception.Message, EventType);
        StringAssert.Contains(exception.Message, nameof(EventDeliveryOwner.LegacyPolling));
        StringAssert.Contains(exception.Message, nameof(EventDeliveryOwner.CdcKafka));
    }

    [TestMethod]
    public void LegacyAdapter_uses_stable_consumer_name_and_handler_metadata()
    {
        var handler = new TenantChangedHandler();
        var adapter = new LegacyIntegrationEventHandlerSubscriptionAdapter(handler);

        Assert.AreEqual(
            LegacyIntegrationEventHandlerSubscriptionAdapter.LegacyConsumerName,
            adapter.ConsumerName);
        Assert.AreEqual(handler.EventType, adapter.EventType);
        Assert.AreEqual(handler.SchemaVersion, adapter.SchemaVersion);
        Assert.AreSame(handler, adapter.Handler);
    }

    [TestMethod]
    public void LegacyAdapter_can_share_consumer_name_across_distinct_routes()
    {
        var topic = CreateTopic(EventDeliveryOwner.LegacyPolling);
        var changedTopic = IntegrationEventTopicDefinition.Create(
            "tenancy.tenant-provisioned.v1",
            "fullnet.tenancy.tenant.provisioned",
            1,
            EventDeliveryOwner.LegacyPolling);
        var first = new LegacyIntegrationEventHandlerSubscriptionAdapter(new TenantChangedHandler());
        var second = new LegacyIntegrationEventHandlerSubscriptionAdapter(new TenantProvisionedHandler());

        var catalog = new IntegrationEventSubscriptionCatalog(
            [topic, changedTopic],
            [first, second]);

        Assert.AreSame(
            first,
            catalog.GetRequired(
                LegacyIntegrationEventHandlerSubscriptionAdapter.LegacyConsumerName,
                first.EventType,
                1));
        Assert.AreSame(
            second,
            catalog.GetRequired(
                LegacyIntegrationEventHandlerSubscriptionAdapter.LegacyConsumerName,
                second.EventType,
                1));
    }

    private static IntegrationEventTopicDefinition CreateTopic(EventDeliveryOwner owner) =>
        IntegrationEventTopicDefinition.Create(
            TopicCode,
            EventType,
            1,
            owner);

    private sealed class TestSubscription(
        string consumerName,
        string eventType,
        int schemaVersion)
        : IIntegrationEventSubscription
    {
        public string ConsumerName { get; } = consumerName;

        public string EventType { get; } = eventType;

        public int SchemaVersion { get; } = schemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TenantChangedHandler : IIntegrationEventHandler
    {
        public string EventType => "fullnet.tenancy.tenant.changed";

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TenantProvisionedHandler : IIntegrationEventHandler
    {
        public string EventType => "fullnet.tenancy.tenant.provisioned";

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
