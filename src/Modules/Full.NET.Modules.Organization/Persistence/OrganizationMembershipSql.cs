using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>跨模块机构成员关系只读 SQL。</summary>
internal static class OrganizationMembershipSql
{
    public static readonly SqlStatement CountActiveMembership = new(
        "organization.membership.count_active",
        """
        SELECT COUNT(1)
        FROM fn_organization_user_unit
        WHERE TenantId = @TenantId
          AND UnitId = @OrganizationUnitId
          AND UserId = @UserId
          AND IsActive = 1
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListActiveMembershipsByUser = new(
        "organization.membership.list_active_by_user",
        """
        SELECT TenantId, UnitId AS OrganizationUnitId
        FROM fn_organization_user_unit
        WHERE UserId = @UserId
          AND IsActive = 1
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListActiveUsersByTenantAndUnit = new(
        "organization.membership.list_active_users_by_tenant_unit",
        """
        SELECT UserId
        FROM fn_organization_user_unit
        WHERE TenantId = @TenantId
          AND UnitId = @OrganizationUnitId
          AND IsActive = 1
        """,
        SqlDataScope.Global);
    public static readonly SqlStatement CountDistinctActiveUsersSqlServer = new(
        "organization.membership.count_distinct_active_users.sqlserver",
        """
        SELECT COUNT(DISTINCT membership.UserId)
        FROM fn_organization_user_unit membership
        WHERE membership.IsActive = 1 AND EXISTS (
            SELECT 1 FROM OPENJSON(@Targets) WITH (
                TenantId uniqueidentifier '$.tenantId', OrganizationUnitId uniqueidentifier '$.organizationUnitId') target
            WHERE target.TenantId = membership.TenantId AND target.OrganizationUnitId = membership.UnitId)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountDistinctActiveUsersMySql = new(
        "organization.membership.count_distinct_active_users.mysql",
        """
        SELECT COUNT(DISTINCT membership.UserId)
        FROM fn_organization_user_unit membership
        WHERE membership.IsActive = 1 AND EXISTS (
            SELECT 1 FROM JSON_TABLE(@Targets, '$[*]' COLUMNS (
                TenantId VARCHAR(36) PATH '$.tenantId', OrganizationUnitId VARCHAR(36) PATH '$.organizationUnitId')) target
            WHERE UNHEX(REPLACE(target.TenantId, '-', '')) = membership.TenantId
              AND UNHEX(REPLACE(target.OrganizationUnitId, '-', '')) = membership.UnitId)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountActiveTargetUnitsSqlServer = new(
        "organization.units.count_active_targets.sqlserver",
        """
        SELECT COUNT(1) FROM fn_organization_unit unitObject
        WHERE unitObject.IsActive = 1 AND EXISTS (
            SELECT 1 FROM OPENJSON(@Targets) WITH (TenantId uniqueidentifier '$.tenantId', OrganizationUnitId uniqueidentifier '$.organizationUnitId') target
            WHERE target.TenantId = unitObject.TenantId AND target.OrganizationUnitId = unitObject.Id)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountActiveTargetUnitsMySql = new(
        "organization.units.count_active_targets.mysql",
        """
        SELECT COUNT(1) FROM fn_organization_unit unitObject
        WHERE unitObject.IsActive = 1 AND EXISTS (
            SELECT 1 FROM JSON_TABLE(@Targets, '$[*]' COLUMNS (TenantId VARCHAR(36) PATH '$.tenantId', OrganizationUnitId VARCHAR(36) PATH '$.organizationUnitId')) target
            WHERE UNHEX(REPLACE(target.TenantId, '-', '')) = unitObject.TenantId AND UNHEX(REPLACE(target.OrganizationUnitId, '-', '')) = unitObject.Id)
        """, SqlDataScope.Global);
}
