using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>候选与总数使用同一可信租户和启用条件；已有租户编码唯一索引支撑稳定分页。</summary>
internal static class CurrentTenantOrganizationUnitDirectorySql
{
    private const string Filter = " WHERE TenantId = @TenantId AND IsActive = 1";
    private const string List = "SELECT Id, Code, Name FROM fn_organization_unit";
    internal static readonly SqlStatement Find = new("organization.current_tenant_unit_directory.find",
        List + Filter + " AND Id IN @UnitIds", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UserUnits = new("organization.current_tenant_unit_directory.user_units",
        """
        SELECT DISTINCT unit.Id
        FROM fn_organization_user_unit AS membership
        INNER JOIN fn_organization_unit AS unit ON unit.Id = membership.UnitId
        WHERE membership.TenantId = @TenantId AND unit.TenantId = @TenantId
          AND membership.UserId = @UserId AND membership.IsActive = 1 AND unit.IsActive = 1
        ORDER BY unit.Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Count = new("organization.current_tenant_unit_directory.count",
        "SELECT COUNT(*) FROM fn_organization_unit" + Filter, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServer = new("organization.current_tenant_unit_directory.list_sqlserver",
        List + Filter + " ORDER BY Code, Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySql = new("organization.current_tenant_unit_directory.list_mysql",
        List + Filter + " ORDER BY Code, Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>固定列序的最小机构候选投影，原生路径显式注册物化器。</summary>
internal sealed record CurrentTenantOrganizationUnitDirectoryRow(Guid Id, string Code, string Name);
