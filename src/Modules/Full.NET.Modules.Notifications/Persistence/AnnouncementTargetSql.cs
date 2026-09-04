using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>公告受众子表的参数化 SQL 语句集合。</summary>
internal static class AnnouncementTargetSql
{
    public static readonly SqlStatement ListUsersByAnnouncementIds =
        new(
            "notifications.list_announcement_target_users",
            """
            SELECT Id, AnnouncementId, UserId
            FROM fn_notifications_announcement_target_user
            WHERE AnnouncementId IN @AnnouncementIds
            ORDER BY AnnouncementId, UserId
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListOrganizationsByAnnouncementIds =
        new(
            "notifications.list_announcement_target_organizations",
            """
            SELECT Id, AnnouncementId, TenantId, OrganizationUnitId
            FROM fn_notifications_announcement_target_organization
            WHERE AnnouncementId IN @AnnouncementIds
            ORDER BY AnnouncementId, TenantId, OrganizationUnitId
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteUsersByAnnouncementId =
        new(
            "notifications.delete_announcement_target_users",
            """
            DELETE FROM fn_notifications_announcement_target_user
            WHERE AnnouncementId = @AnnouncementId
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteOrganizationsByAnnouncementId =
        new(
            "notifications.delete_announcement_target_organizations",
            """
            DELETE FROM fn_notifications_announcement_target_organization
            WHERE AnnouncementId = @AnnouncementId
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertUser =
        new(
            "notifications.insert_announcement_target_user",
            """
            INSERT INTO fn_notifications_announcement_target_user
                (Id, AnnouncementId, UserId)
            VALUES
                (@Id, @AnnouncementId, @UserId)
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertOrganization =
        new(
            "notifications.insert_announcement_target_organization",
            """
            INSERT INTO fn_notifications_announcement_target_organization
                (Id, AnnouncementId, TenantId, OrganizationUnitId)
            VALUES
                (@Id, @AnnouncementId, @TenantId, @OrganizationUnitId)
            """,
            SqlDataScope.HostOnly);
}
