using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationsAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_exact_host_announcement_permissions_and_actions()
    {
        var catalog = AuthorizationCatalog.Create([new NotificationsAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                HostAnnouncementPermissions.Create,
                HostAnnouncementPermissions.Publish,
                HostAnnouncementPermissions.Read,
                HostAnnouncementPermissions.Update,
                InboxPermissions.MarkAllRead,
                InboxPermissions.MarkRead,
                InboxPermissions.Read,
                InboxPermissions.Send,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var hostAnnouncements = catalog.Navigation.Single(item => item.Id == "host-announcements");
        Assert.AreEqual(HostAnnouncementPermissions.Read, hostAnnouncements.RequiredPermission);

        CollectionAssert.AreEqual(
            new[]
            {
                HostAnnouncementPermissions.Create,
                HostAnnouncementPermissions.Update,
                HostAnnouncementPermissions.Publish,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "host-announcements")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());

        var inboxMessages = catalog.Navigation.Single(item => item.Id == "inbox-messages");
        Assert.AreEqual(InboxPermissions.Read, inboxMessages.RequiredPermission);

        CollectionAssert.AreEqual(
            new[]
            {
                InboxPermissions.Send,
                InboxPermissions.MarkRead,
                InboxPermissions.MarkAllRead,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "inbox-messages")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());
    }
}