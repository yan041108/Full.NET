using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Notifications.Features.ManageMyHostAnnouncements;

/// <summary>判断 Host 用户是否属于公告受众；机构受众通过 Organization 只读端口解析。</summary>
internal sealed class HostAnnouncementRecipientGuard(
    ITenantOrganizationUserMembershipReader membershipReader)
{
    /// <summary>判断用户是否可接收指定公告。</summary>
    public async Task<bool> IsRecipientAsync(
        Guid userId,
        AnnouncementRecord announcement,
        AnnouncementTargetBundle targets,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                announcement.Status,
                AnnouncementStatuses.Published,
                StringComparison.Ordinal))
        {
            return false;
        }

        return announcement.AudienceKind switch
        {
            AnnouncementAudienceKinds.All => true,
            AnnouncementAudienceKinds.Users => targets.GetUserIds(announcement.Id).Contains(userId),
            AnnouncementAudienceKinds.Organizations => await IsOrganizationRecipientAsync(
                userId,
                targets.GetOrganizations(announcement.Id),
                cancellationToken).ConfigureAwait(false),
            _ => false,
        };
    }

    /// <summary>从机构受众候选中过滤当前用户可见公告。</summary>
    public async Task<HashSet<Guid>> FilterVisibleOrganizationAnnouncementsAsync(
        Guid userId,
        IReadOnlyList<ReceivedHostAnnouncementRecord> organizationCandidates,
        AnnouncementTargetBundle targets,
        CancellationToken cancellationToken = default)
    {
        if (organizationCandidates.Count == 0)
        {
            return [];
        }

        var memberships = await membershipReader.ListActiveMembershipsAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        if (memberships.Count == 0)
        {
            return [];
        }

        var membershipKeys = memberships
            .Select(entry => (entry.TenantId, entry.OrganizationUnitId))
            .ToHashSet();
        var visible = new HashSet<Guid>();
        foreach (var candidate in organizationCandidates)
        {
            var organizations = targets.GetOrganizations(candidate.Id);
            if (organizations.Any(target =>
                    membershipKeys.Contains((target.TenantId, target.OrganizationUnitId))))
            {
                visible.Add(candidate.Id);
            }
        }

        return visible;
    }

    private async Task<bool> IsOrganizationRecipientAsync(
        Guid userId,
        IReadOnlyList<HostAnnouncementTargetOrganization> organizations,
        CancellationToken cancellationToken)
    {
        foreach (var organization in organizations)
        {
            if (await membershipReader.IsActiveMemberAsync(
                    organization.TenantId,
                    organization.OrganizationUnitId,
                    userId,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}
