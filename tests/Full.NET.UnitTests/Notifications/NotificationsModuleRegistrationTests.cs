using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.AliyunSms;
using Full.NET.Modules.Notifications.Providers.DingTalk;
using Full.NET.Modules.Notifications.Providers.WeCom;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;
using Full.NET.Modules.Notifications.Features.CreateNotificationIntents;
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
                typeof(WorkflowTodoAssignedIntegrationEventHandler),
                typeof(WorkflowTodoReminderRequestedIntegrationEventHandler),
                typeof(WorkflowTodoEscalationRequestedIntegrationEventHandler),
                typeof(WorkflowInstanceCompletedIntegrationEventHandler),
                typeof(WorkflowInstanceRejectedIntegrationEventHandler),
                typeof(WorkflowInstanceCancelledIntegrationEventHandler),
            },
            handlerTypes);
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(NotificationRealtimeDelivery)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(NotificationRecipientEndpointProtector)
            && descriptor.Lifetime == ServiceLifetime.Singleton));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(WorkflowNotificationTemplateProvisioner)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(NotificationRecipientDirectoryResolver)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
    }

    [TestMethod]
    public void Smtp_provider_is_registered_only_when_explicitly_enabled()
    {
        var disabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            disabled,
            new ConfigurationBuilder().Build());

        var enabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            enabled,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Notifications:Providers:Smtp:Enabled"] = "true",
                })
                .Build());

        Assert.IsFalse(disabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(SmtpNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(SmtpNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationSecretResolver)
            && descriptor.ImplementationType == typeof(EnvironmentNotificationSecretResolver)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(ISmtpMailTransport)
            && descriptor.ImplementationType == typeof(MailKitSmtpTransport)));
    }

    [TestMethod]
    public void Aliyun_sms_provider_is_registered_only_when_explicitly_enabled()
    {
        var disabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            disabled,
            new ConfigurationBuilder().Build());

        var enabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            enabled,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Notifications:Providers:AliyunSms:Enabled"] = "true",
                })
                .Build());

        Assert.IsFalse(disabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(AliyunSmsNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(AliyunSmsNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationReceiptVerifier)
            && descriptor.ImplementationType == typeof(AliyunSmsReceiptVerifier)));
    }

    [TestMethod]
    public void DingTalk_provider_is_registered_only_when_explicitly_enabled()
    {
        var disabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            disabled,
            new ConfigurationBuilder().Build());

        var enabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            enabled,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Notifications:Providers:DingTalk:Enabled"] = "true",
                })
                .Build());

        Assert.IsFalse(disabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(DingTalkNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(DingTalkNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationReceiptVerifier)
            && descriptor.ImplementationType == typeof(DingTalkReceiptVerifier)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(DingTalkAccessTokenCache)
            && descriptor.Lifetime == ServiceLifetime.Singleton));
    }

    [TestMethod]
    public void WeCom_provider_is_registered_only_when_explicitly_enabled()
    {
        var disabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            disabled,
            new ConfigurationBuilder().Build());

        var enabled = new ServiceCollection();
        new NotificationsModule().AddBackgroundServices(
            enabled,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Notifications:Providers:WeCom:Enabled"] = "true",
                })
                .Build());

        Assert.IsFalse(disabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(WeComNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(INotificationProviderAdapter)
            && descriptor.ImplementationType == typeof(WeComNotificationProviderAdapter)));
        Assert.IsTrue(enabled.Any(descriptor =>
            descriptor.ServiceType == typeof(WeComAccessTokenCache)
            && descriptor.Lifetime == ServiceLifetime.Singleton));
    }
}
