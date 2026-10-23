using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>单元测试（消息/Kafka）：Effective Event Delivery Owner Resolver。</summary>
[TestClass]
public sealed class EffectiveEventDeliveryOwnerResolverTests
{
    [TestMethod]
    public void Locked_missing_stream_uses_directory_default_without_reading_store()
    {
        var store = Substitute.For<IEventStreamOwnershipStore>();
        var catalog = new IntegrationEventSubscriptionCatalog([], []);
        var resolver = new EffectiveEventDeliveryOwnerResolver(() => catalog, store);
        Assert.AreEqual(EventDeliveryOwner.LegacyPolling, resolver.ResolveDeliveryOwner("fullnet.notifications.inbox.received", 1, null));
        Assert.AreEqual(0, store.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Unregistered_existing_stream_falls_back_to_legacy_polling()
    {
        var store = Substitute.For<IEventStreamOwnershipStore>();
        store.FindAsync("fullnet.notifications.inbox.received", 1, Arg.Any<CancellationToken>())
            .Returns((EventStreamOwnershipRecord?)null);
        var catalog = new IntegrationEventSubscriptionCatalog([], []);
        var resolver = new EffectiveEventDeliveryOwnerResolver(() => catalog, store);

        var owner = await resolver.GetDeliveryOwnerAsync(
            "fullnet.notifications.inbox.received",
            1,
            CancellationToken.None);

        Assert.AreEqual(EventDeliveryOwner.LegacyPolling, owner);
    }
}
