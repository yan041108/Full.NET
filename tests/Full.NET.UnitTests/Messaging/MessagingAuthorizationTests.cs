using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Messaging;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.UnitTests.Messaging;

/// <summary>单元测试（消息/Kafka）：Messaging Authorization Contributor。</summary>
[TestClass]
public sealed class MessagingAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_exact_messaging_permissions_actions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new MessagingAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                MessagingPermissions.DeadLettersRead,
                MessagingPermissions.DeadLettersReplay,
                MessagingPermissions.DeliveryCutover,
                MessagingPermissions.DeliveryRollback,
                MessagingPermissions.EventsRead,
                MessagingPermissions.KafkaRangeReplay,
                MessagingPermissions.LegacyDeadLettersRequeue,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var navigation = catalog.Navigation.Single(item => item.Id == "host-messaging-ops");
        Assert.AreEqual(MessagingPermissions.DeadLettersRead, navigation.RequiredPermission);

        CollectionAssert.AreEqual(
            new[]
            {
                MessagingPermissions.LegacyDeadLettersRequeue,
                MessagingPermissions.DeadLettersReplay,
                MessagingPermissions.KafkaRangeReplay,
                MessagingPermissions.DeliveryCutover,
                MessagingPermissions.DeliveryRollback,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "host-messaging-ops")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());
    }
}
