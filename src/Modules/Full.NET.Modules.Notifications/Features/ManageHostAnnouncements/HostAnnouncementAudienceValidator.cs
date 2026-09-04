using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;

/// <summary>校验 Host 公告受众配置与跨模块目录投影。</summary>
internal sealed class HostAnnouncementAudienceValidator(
    IHostUserDirectory hostUserDirectory,
    ITenantOrganizationUnitDirectory organizationUnitDirectory)
{
    /// <summary>
    /// 规范化并校验公告类型、受众与子表目标；失败时返回验证错误。
    /// </summary>
    public async Task<Result<HostAnnouncementAudienceState>> ValidateAsync(
        string? kind,
        string? audienceKind,
        IReadOnlyList<Guid>? targetUserIds,
        IReadOnlyList<HostAnnouncementTargetOrganization>? targetOrganizations,
        CancellationToken cancellationToken)
    {
        var normalizedKind = NormalizeKind(kind);
        if (normalizedKind is null)
        {
            return InvalidAudience("Announcement kind must be notice or announcement.");
        }

        var normalizedAudience = NormalizeAudienceKind(audienceKind);
        if (normalizedAudience is null)
        {
            return InvalidAudience("Announcement audience must be all, users, or organizations.");
        }

        var users = NormalizeUserIds(targetUserIds);
        var organizations = NormalizeOrganizations(targetOrganizations);
        if (normalizedAudience == AnnouncementAudienceKinds.All)
        {
            if (users.Count > 0 || organizations.Count > 0)
            {
                return InvalidAudience("All-audience announcements must not include explicit targets.");
            }

            return Result<HostAnnouncementAudienceState>.Success(
                new HostAnnouncementAudienceState(normalizedKind, normalizedAudience, users, organizations));
        }

        if (normalizedAudience == AnnouncementAudienceKinds.Users)
        {
            if (organizations.Count > 0)
            {
                return InvalidAudience("User-audience announcements must not include organization targets.");
            }

            if (users.Count == 0)
            {
                return InvalidAudience("User-audience announcements require at least one target user.");
            }

            foreach (var userId in users)
            {
                var entry = await hostUserDirectory.FindActiveHostUserAsync(userId, cancellationToken)
                    .ConfigureAwait(false);
                if (entry is null)
                {
                    return InvalidAudience("One or more target users do not exist or are disabled.");
                }
            }

            return Result<HostAnnouncementAudienceState>.Success(
                new HostAnnouncementAudienceState(normalizedKind, normalizedAudience, users, organizations));
        }

        if (users.Count > 0)
        {
            return InvalidAudience("Organization-audience announcements must not include user targets.");
        }

        if (organizations.Count == 0)
        {
            return InvalidAudience("Organization-audience announcements require at least one target organization.");
        }

        foreach (var target in organizations)
        {
            var entry = await organizationUnitDirectory.FindActiveUnitAsync(
                    target.TenantId,
                    target.OrganizationUnitId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (entry is null)
            {
                return InvalidAudience("One or more target organizations do not exist or are disabled.");
            }
        }

        return Result<HostAnnouncementAudienceState>.Success(
            new HostAnnouncementAudienceState(normalizedKind, normalizedAudience, users, organizations));
    }

    internal static string? NormalizeKind(string? kind)
    {
        var normalized = string.IsNullOrWhiteSpace(kind)
            ? AnnouncementKinds.Announcement
            : kind.Trim();
        return normalized is AnnouncementKinds.Notice or AnnouncementKinds.Announcement
            ? normalized
            : null;
    }

    internal static string? NormalizeAudienceKind(string? audienceKind)
    {
        var normalized = string.IsNullOrWhiteSpace(audienceKind)
            ? AnnouncementAudienceKinds.All
            : audienceKind.Trim();
        return normalized is AnnouncementAudienceKinds.All
            or AnnouncementAudienceKinds.Users
            or AnnouncementAudienceKinds.Organizations
            ? normalized
            : null;
    }

    internal static IReadOnlyList<Guid> NormalizeUserIds(IReadOnlyList<Guid>? targetUserIds)
    {
        if (targetUserIds is null || targetUserIds.Count == 0)
        {
            return [];
        }

        return targetUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    internal static IReadOnlyList<HostAnnouncementTargetOrganization> NormalizeOrganizations(
        IReadOnlyList<HostAnnouncementTargetOrganization>? targetOrganizations)
    {
        if (targetOrganizations is null || targetOrganizations.Count == 0)
        {
            return [];
        }

        return targetOrganizations
            .Where(target => target.TenantId != Guid.Empty && target.OrganizationUnitId != Guid.Empty)
            .DistinctBy(target => (target.TenantId, target.OrganizationUnitId))
            .OrderBy(target => target.TenantId)
            .ThenBy(target => target.OrganizationUnitId)
            .ToArray();
    }

    private static Result<HostAnnouncementAudienceState> InvalidAudience(string message) =>
        Result<HostAnnouncementAudienceState>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementAudienceInvalid,
            message,
            ErrorType.Validation));
}

/// <summary>已校验的公告受众状态快照。</summary>
/// <param name="Kind">公告类型机器码。</param>
/// <param name="AudienceKind">受众范围机器码。</param>
/// <param name="TargetUserIds">去重后的目标用户标识。</param>
/// <param name="TargetOrganizations">去重后的目标机构。</param>
internal sealed record HostAnnouncementAudienceState(
    string Kind,
    string AudienceKind,
    IReadOnlyList<Guid> TargetUserIds,
    IReadOnlyList<HostAnnouncementTargetOrganization> TargetOrganizations);
