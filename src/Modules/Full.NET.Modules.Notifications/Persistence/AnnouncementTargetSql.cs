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
    public static readonly SqlStatement InsertUsersSqlServer = new(
        "notifications.insert_announcement_targets.users.sqlserver",
        """
        INSERT INTO fn_notifications_announcement_target_user (Id, AnnouncementId, UserId)
        SELECT rowData.Id, @AnnouncementId, rowData.UserId FROM OPENJSON(@Rows) WITH (Id uniqueidentifier '$.id', UserId uniqueidentifier '$.userId') AS rowData
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertUsersMySql = new(
        "notifications.insert_announcement_targets.users.mysql",
        """
        INSERT INTO fn_notifications_announcement_target_user (Id, AnnouncementId, UserId)
        SELECT UNHEX(REPLACE(rowData.Id, '-', '')), @AnnouncementId, UNHEX(REPLACE(rowData.UserId, '-', '')) FROM JSON_TABLE(@Rows, '$[*]' COLUMNS (Id CHAR(36) PATH '$.id', UserId CHAR(36) PATH '$.userId')) AS rowData
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertOrganizationsSqlServer = new(
        "notifications.insert_announcement_targets.organizations.sqlserver",
        """
        INSERT INTO fn_notifications_announcement_target_organization (Id, AnnouncementId, TenantId, OrganizationUnitId)
        SELECT rowData.Id, @AnnouncementId, rowData.TenantId, rowData.OrganizationUnitId FROM OPENJSON(@Rows) WITH (Id uniqueidentifier '$.id', TenantId uniqueidentifier '$.tenantId', OrganizationUnitId uniqueidentifier '$.organizationUnitId') AS rowData
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertOrganizationsMySql = new(
        "notifications.insert_announcement_targets.organizations.mysql",
        """
        INSERT INTO fn_notifications_announcement_target_organization (Id, AnnouncementId, TenantId, OrganizationUnitId)
        SELECT UNHEX(REPLACE(rowData.Id, '-', '')), @AnnouncementId, UNHEX(REPLACE(rowData.TenantId, '-', '')), UNHEX(REPLACE(rowData.OrganizationUnitId, '-', '')) FROM JSON_TABLE(@Rows, '$[*]' COLUMNS (Id CHAR(36) PATH '$.id', TenantId CHAR(36) PATH '$.tenantId', OrganizationUnitId CHAR(36) PATH '$.organizationUnitId')) AS rowData
        """, SqlDataScope.HostOnly);
}
