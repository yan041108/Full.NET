using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationPlatformAuthorizationTests
{
    [TestMethod]
    public void Platform_page_and_action_permissions_are_independent_closed_codes()
    {
        var contributor = new NotificationsAuthorizationContributor();
        var catalog = AuthorizationCatalog.Create([contributor]);

        CollectionAssert.IsSubsetOf(
            NotificationPlatformPermissions.All.ToArray(),
            contributor.Permissions.Select(permission => permission.Code).ToArray());
        Assert.IsFalse(NotificationPlatformPermissions.All.Any(code => code.Contains('/', StringComparison.Ordinal)));
        Assert.AreEqual(
            NotificationPlatformPermissions.All.Count,
            NotificationPlatformPermissions.All.Distinct(StringComparer.Ordinal).Count());

        Assert.AreEqual(
            NotificationPlatformPermissions.TemplatesRead,
            catalog.Navigation.Single(item => item.Id == "notification-templates").RequiredPermission);
        Assert.AreEqual(
            NotificationPlatformPermissions.ProviderProfilesRead,
            catalog.Navigation.Single(item => item.Id == "notification-provider-profiles").RequiredPermission);
        Assert.AreEqual(
            NotificationPlatformPermissions.BindingsRead,
            catalog.Navigation.Single(item => item.Id == "notification-bindings").RequiredPermission);
        Assert.AreEqual(
            NotificationPlatformPermissions.DeliveriesRead,
            catalog.Navigation.Single(item => item.Id == "notification-deliveries").RequiredPermission);
        Assert.AreEqual(
            NotificationPlatformPermissions.PreferencesRead,
            catalog.Navigation.Single(item => item.Id == "notification-preferences").RequiredPermission);

        CollectionAssert.Contains(
            catalog.Actions.Select(action => action.PermissionCode).ToArray(),
            "notifications.provider_profiles.publish");
    }

    [TestMethod]
    public void Unknown_platform_permission_codes_fail_closed()
    {
        var known = new HashSet<string>(NotificationPlatformPermissions.All, StringComparer.Ordinal);
        Assert.IsFalse(known.Contains("notifications.templates.read/update"));
        Assert.IsFalse(known.Contains("notifications.deliveries.write"));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => AuthorizationCatalog.Create(
            [
                new NotificationsAuthorizationContributor(),
                new UnpublishedDeliveryWriteActionContributor(),
            ]));
    }

    private sealed class UnpublishedDeliveryWriteActionContributor
        : Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor
    {
        public Full.NET.Modules.Identity.Contracts.AuthorizationModuleDefinition Module { get; } =
            new("notifications-unknown", "未知权限", 99);

        public IReadOnlyCollection<Full.NET.Modules.Identity.Contracts.PermissionDefinition> Permissions { get; } = [];

        public IReadOnlyCollection<Full.NET.Modules.Identity.Contracts.NavigationDefinition> Navigation { get; } = [];

        public IReadOnlyCollection<Full.NET.Modules.Identity.Contracts.AuthorizationActionDefinition> Actions { get; } =
            [
                new(
                    "notifications.deliveries.write",
                    "notification-deliveries",
                    "notifications.deliveries.write",
                    "未知投递写操作",
                    "write",
                    90),
            ];
    }
}
