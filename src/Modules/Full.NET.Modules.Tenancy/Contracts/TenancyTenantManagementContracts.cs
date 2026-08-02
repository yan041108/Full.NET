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

    /// <summary>开通 Host 租户。</summary>
    public const string Create = "tenancy.tenants.create";

    /// <summary>更新 Host 租户显示名称。</summary>
    public const string Update = "tenancy.tenants.update";

    /// <summary>禁用 Host 租户。</summary>
    public const string Disable = "tenancy.tenants.disable";

    /// <summary>为 Host 租户分配或解除套餐绑定。</summary>
    public const string AssignPackage = "tenancy.tenants.assign_package";

    /// <summary>迁移 060 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "tenancy.tenants.write";
}

/// <summary>更新 Host 租户显示名称请求；标识与域名创建后不可变。</summary>
public sealed record UpdateHostTenantRequest(
    string Name,
    int Version);

/// <summary>为 Host 租户分配或解除套餐绑定；null 表示解除。</summary>
public sealed record AssignHostTenantPackageRequest(
    Guid? TenantPackageId,
    int Version);
