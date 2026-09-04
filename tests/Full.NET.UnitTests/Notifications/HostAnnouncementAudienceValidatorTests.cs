using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Organization.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class HostAnnouncementAudienceValidatorTests
{
    [TestMethod]
    public async Task Validate_rejects_user_audience_without_targets()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            AnnouncementKinds.Notice,
            AnnouncementAudienceKinds.Users,
            [],
            null,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            NotificationsErrorCodes.AnnouncementAudienceInvalid,
            result.Error?.Code);
    }

    [TestMethod]
    public async Task Validate_rejects_unknown_host_user_targets()
    {
        var hostUsers = Substitute.For<IHostUserDirectory>();
        hostUsers.FindActiveHostUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((HostUserDirectoryEntry?)null);
        var validator = new HostAnnouncementAudienceValidator(
            hostUsers,
            Substitute.For<ITenantOrganizationUnitDirectory>());
        var userId = Guid.CreateVersion7();

        var result = await validator.ValidateAsync(
            null,
            AnnouncementAudienceKinds.Users,
            [userId],
            null,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            NotificationsErrorCodes.AnnouncementAudienceInvalid,
            result.Error?.Code);
    }

    [TestMethod]
    public async Task Validate_accepts_all_audience_without_targets()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AnnouncementKinds.Announcement, result.Value?.Kind);
        Assert.AreEqual(AnnouncementAudienceKinds.All, result.Value?.AudienceKind);
        Assert.AreEqual(0, result.Value?.TargetUserIds.Count);
    }

    private static HostAnnouncementAudienceValidator CreateValidator()
    {
        var hostUsers = Substitute.For<IHostUserDirectory>();
        hostUsers.FindActiveHostUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HostUserDirectoryEntry(Guid.CreateVersion7(), "demo", "Demo"));
        return new HostAnnouncementAudienceValidator(
            hostUsers,
            Substitute.For<ITenantOrganizationUnitDirectory>());
    }
}
