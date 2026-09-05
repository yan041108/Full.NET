using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features;
using Full.NET.Modules.Notifications.Features.CreateNotificationIntents;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationRecipientDirectoryResolverTests
{
    [TestMethod]
    public async Task Host_scope_resolves_all_recipients_with_one_host_batch_query()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        hostUsers.FindActiveHostUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, HostUserDirectoryEntry>
            {
                [first] = new(first, "first", "第一位用户", LocaleCatalog.English),
                [second] = new(second, "second", "第二位用户", LocaleCatalog.Chinese),
            });
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        var service = new NotificationRecipientDirectoryResolver(hostUsers, tenantUsers);

        var result = await service.ResolveAsync(
            NotificationInboxScope.FromTrustedTenantId(null),
            CreateRecipients(first, second),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(first, result.Value![0].UserId);
        Assert.AreEqual(LocaleCatalog.English, result.Value[0].PreferredLocale);
        Assert.AreEqual(second, result.Value[1].UserId);
        Assert.AreEqual(LocaleCatalog.Chinese, result.Value[1].PreferredLocale);
        await hostUsers.Received(1).FindActiveHostUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids != null && ids.SequenceEqual(new[] { first, second })),
            Arg.Any<CancellationToken>());
        await tenantUsers.DidNotReceive().FindActiveTenantUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tenant_scope_resolves_recipients_only_from_current_tenant_batch_directory()
    {
        var userId = Guid.CreateVersion7();
        var hostUsers = Substitute.For<IHostUserBatchSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        tenantUsers.FindActiveTenantUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry>
            {
                [userId] = new(userId, "tenant-user", "租户用户", LocaleCatalog.English),
            });
        var service = new NotificationRecipientDirectoryResolver(hostUsers, tenantUsers);

        var result = await service.ResolveAsync(
            NotificationInboxScope.FromTrustedTenantId(Guid.CreateVersion7()),
            CreateRecipients(userId),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var resolved = result.Value!;
        Assert.AreEqual(userId, resolved.Single().UserId);
        Assert.AreEqual(LocaleCatalog.English, resolved.Single().PreferredLocale);
        await tenantUsers.Received(1).FindActiveTenantUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids != null && ids.SequenceEqual(new[] { userId })),
            Arg.Any<CancellationToken>());
        await hostUsers.DidNotReceive().FindActiveHostUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tenant_scope_rejects_recipient_missing_from_current_tenant_directory()
    {
        var requested = Guid.CreateVersion7();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        tenantUsers.FindActiveTenantUsersAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TenantUserDirectoryEntry>());
        var service = new NotificationRecipientDirectoryResolver(
            Substitute.For<IHostUserBatchSelectionDirectory>(),
            tenantUsers);

        var result = await service.ResolveAsync(
            NotificationInboxScope.FromTrustedTenantId(Guid.CreateVersion7()),
            CreateRecipients(requested),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("notifications.inbox_recipient_not_found", result.Error!.Code);
    }

    private static NotificationRecipientInput[] CreateRecipients(params Guid[] userIds) =>
        userIds
            .Select(userId => new NotificationRecipientInput("user", userId.ToString("N")))
            .ToArray();
}
