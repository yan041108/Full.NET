using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>活动普通角色候选只读 Identity 自有表；总数和列表使用同一范围过滤。</summary>
internal static class ActiveRoleDirectorySql
{
    private const string Host = "ScopeKey = 'host' AND TenantId IS NULL AND IsActive = 1 AND IsSuperAdministrator = 0";
    private const string Tenant = "TenantId = @TenantId AND ScopeKey = @TenantScopeKey AND IsActive = 1 AND IsSuperAdministrator = 0";
    private const string List = "SELECT Id, Code, Name FROM fn_identity_role WHERE ";
    private const string Count = "SELECT COUNT(*) FROM fn_identity_role WHERE ";
    // 编码与主键固定排序；SQL Server 和 MySQL 分别使用自己的有界分页语法。
    private const string SqlServerPage = " ORDER BY Code, Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
    private const string MySqlPage = " ORDER BY Code, Id LIMIT @PageSize OFFSET @Offset";
    internal static readonly SqlStatement CountHost = new("identity.active_role_directory.count_host", Count + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountTenant = new("identity.active_role_directory.count_tenant", Count + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServerHost = new("identity.active_role_directory.list_sqlserver_host", List + Host + SqlServerPage, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("identity.active_role_directory.list_sqlserver_tenant", List + Tenant + SqlServerPage, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("identity.active_role_directory.list_mysql_host", List + Host + MySqlPage, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("identity.active_role_directory.list_mysql_tenant", List + Tenant + MySqlPage, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>有固定列名的角色候选投影，原生路径显式注册物化器。</summary>
internal sealed record ActiveRoleDirectoryRow(Guid Id, string Code, string Name);
