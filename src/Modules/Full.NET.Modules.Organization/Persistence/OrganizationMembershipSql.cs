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
}
