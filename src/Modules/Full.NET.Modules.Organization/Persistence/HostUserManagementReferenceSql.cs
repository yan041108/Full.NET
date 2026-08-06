using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>Host 用户管理页按租户读取机构参考数据时使用的 Global 租户探测 SQL。</summary>
internal static class HostUserManagementReferenceSql
{
    /// <summary>Host 用户管理页读取租户有效机构，不受操作者机构数据范围裁剪。</summary>
    public static readonly SqlStatement ListUnits = new(
        "organization.host_user_management.list_units",
        """
        SELECT Id, ParentId, Code, Name, DisplayOrder, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_organization_unit
        WHERE TenantId = @TenantId
          AND IsActive = 1
        ORDER BY DisplayOrder, Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>Host 用户管理页读取租户有效用户-机构隶属，不受操作者机构数据范围裁剪。</summary>
    public static readonly SqlStatement ListUserUnits = new(
        "organization.host_user_management.list_user_units",
        """
        SELECT assignment.Id, assignment.UserId,
               assignment.UnitId, unitObject.Code AS UnitCode, unitObject.Name AS UnitName,
               assignment.IsPrimary, assignment.IsActive,
               assignment.CreatedAtUtc, assignment.UpdatedAtUtc, assignment.Version
        FROM fn_organization_user_unit AS assignment
        INNER JOIN fn_organization_unit AS unitObject
            ON unitObject.Id = assignment.UnitId
           AND unitObject.TenantId = assignment.TenantId
           AND unitObject.IsActive = 1
        WHERE assignment.TenantId = @TenantId
          AND assignment.IsActive = 1
        ORDER BY assignment.IsPrimary DESC, unitObject.DisplayOrder, unitObject.Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
