using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class EffectiveEventDeliveryOwnerResolverTests
{
    [TestMethod]
    public async Task Unregistered_existing_stream_falls_back_to_legacy_polling()
    {
        var store = Substitute.For<IEventStreamOwnershipStore>();
        store.FindAsync("fullnet.notifications.inbox.received", 1, Arg.Any<CancellationToken>())
            .Returns((EventStreamOwnershipRecord?)null);
        var catalog = new IntegrationEventSubscriptionCatalog([], []);
        var resolver = new EffectiveEventDeliveryOwnerResolver(catalog, store);

        var owner = await resolver.GetDeliveryOwnerAsync(
            "fullnet.notifications.inbox.received",
            1,
            CancellationToken.None);

        Assert.AreEqual(EventDeliveryOwner.LegacyPolling, owner);
    }
}
