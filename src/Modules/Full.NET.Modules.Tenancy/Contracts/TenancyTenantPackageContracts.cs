namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>Host 作用域租户套餐目录 API 的权限与契约。</summary>
public static class TenancyTenantPackagePermissions
{
    /// <summary>分页查询套餐目录与详情。</summary>
    public const string Read = "tenancy.tenant_packages.read";

    /// <summary>创建 Host 租户套餐。</summary>
    public const string Create = "tenancy.tenant_packages.create";

    /// <summary>更新 Host 租户套餐显示信息。</summary>
    public const string Update = "tenancy.tenant_packages.update";

    /// <summary>禁用 Host 租户套餐。</summary>
    public const string Disable = "tenancy.tenant_packages.disable";

    /// <summary>迁移 061 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "tenancy.tenant_packages.write";
}

/// <summary>租户套餐摘要；编码创建后不可变。</summary>
public sealed record TenantPackageSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int Version,
    int AssignedTenantCount = 0);

/// <summary>创建 Host 租户套餐请求。</summary>
public sealed record CreateHostTenantPackageRequest(
    string Code,
    string Name,
    string? Description);

/// <summary>更新 Host 租户套餐显示信息请求。</summary>
public sealed record UpdateHostTenantPackageRequest(
    string Name,
    string? Description,
    int Version);
