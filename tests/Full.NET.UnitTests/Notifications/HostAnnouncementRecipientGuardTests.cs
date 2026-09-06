using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Features.ManageMyHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class HostAnnouncementRecipientGuardTests
{
    [TestMethod]
    public async Task Organization_audience_matches_active_membership()
    {
        var tenantId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var announcementId = Guid.CreateVersion7();
        var guard = new HostAnnouncementRecipientGuard(new FakeMembershipReader(
            [new TenantOrganizationMembershipEntry(tenantId, unitId)]));
        var announcement = new AnnouncementRecord
        {
            Id = announcementId,
            AudienceKind = AnnouncementAudienceKinds.Organizations,
            Status = AnnouncementStatuses.Published,
        };
        var targets = AnnouncementTargetBundle.From(
            [],
            [new AnnouncementTargetOrganizationRecord
            {
                Id = Guid.CreateVersion7(),
                AnnouncementId = announcementId,
                TenantId = tenantId,
                OrganizationUnitId = unitId,
            }]);

        var isRecipient = await guard.IsRecipientAsync(userId, announcement, targets);
        Assert.IsTrue(isRecipient);
    }

    [TestMethod]
    public async Task Organization_candidates_filter_to_visible_announcements_only()
    {
        var tenantId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var visibleId = Guid.CreateVersion7();
        var hiddenId = Guid.CreateVersion7();
        var guard = new HostAnnouncementRecipientGuard(new FakeMembershipReader(
            [new TenantOrganizationMembershipEntry(tenantId, unitId)]));
        var candidates =
            new[]
            {
                new ReceivedHostAnnouncementRecord { Id = visibleId, AudienceKind = AnnouncementAudienceKinds.Organizations },
                new ReceivedHostAnnouncementRecord { Id = hiddenId, AudienceKind = AnnouncementAudienceKinds.Organizations },
            };
        var targets = AnnouncementTargetBundle.From(
            [],
            [
                new AnnouncementTargetOrganizationRecord
                {
                    Id = Guid.CreateVersion7(),
                    AnnouncementId = visibleId,
                    TenantId = tenantId,
                    OrganizationUnitId = unitId,
                },
                new AnnouncementTargetOrganizationRecord
                {
                    Id = Guid.CreateVersion7(),
                    AnnouncementId = hiddenId,
                    TenantId = Guid.CreateVersion7(),
                    OrganizationUnitId = Guid.CreateVersion7(),
                },
            ]);

        var visible = await guard.FilterVisibleOrganizationAnnouncementsAsync(
            userId,
            candidates,
            targets);

        CollectionAssert.AreEquivalent(new[] { visibleId }, visible.ToArray());
    }

    private sealed class FakeMembershipReader(
        IReadOnlyList<TenantOrganizationMembershipEntry> memberships)
        : ITenantOrganizationUserMembershipReader
    {
        public Task<bool> IsActiveMemberAsync(
            Guid tenantId,
            Guid organizationUnitId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(memberships.Any(entry =>
                entry.TenantId == tenantId
                && entry.OrganizationUnitId == organizationUnitId));

        public Task<IReadOnlyList<TenantOrganizationMembershipEntry>> ListActiveMembershipsAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(memberships);

        public Task<long> CountDistinctActiveMembersAsync(
            IReadOnlyList<TenantOrganizationMembershipTarget> organizationTargets,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((long)organizationTargets.Count);
    }
}
