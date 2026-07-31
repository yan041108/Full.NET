using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationsModuleRegistrationTests
{
    [TestMethod]
    public void Background_services_register_exact_repair_routes()
    {
        var services = new ServiceCollection();

        new NotificationsModule().AddBackgroundServices(
            services,
            new ConfigurationBuilder().Build());

        var handlerTypes = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IIntegrationEventHandler))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                typeof(AnnouncementPublishedRealtimeHandler),
                typeof(InboxMessageReceivedRealtimeHandler),
                typeof(InboxReadStateChangedRealtimeHandler),
            },
            handlerTypes);
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(NotificationRealtimeDelivery)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
    }
}
