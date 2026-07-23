namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// Host 作用域租户管理 API 的权限与写请求契约。
/// </summary>
public static class TenancyTenantManagementPermissions
{
    /// <summary>枚举可用租户与租户上下文入口（不等同于 Host 租户目录管理）。</summary>
    public const string Read = "tenancy.tenants.read";

    /// <summary>分页查询 Host 租户目录与详情。</summary>
    public const string HostTenantsRead = "tenancy.host_tenants.read";

    /// <summary>创建、更新与禁用租户。</summary>
    public const string Write = "tenancy.tenants.write";
}

/// <summary>更新 Host 租户显示名称请求；标识与域名创建后不可变。</summary>
public sealed record UpdateHostTenantRequest(
    string Name,
    int Version);
