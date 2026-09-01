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
/// <param name="Id">套餐稳定标识。</param>
/// <param name="Code">稳定套餐编码；在 Host 作用域内唯一且创建后不可变。</param>
/// <param name="Name">套餐显示名称。</param>
/// <param name="Description">套餐说明文本；可省略。</param>
/// <param name="IsActive">是否处于活动状态；禁用套餐不可再分配。</param>
/// <param name="Version">乐观并发版本；写操作须回传以避免覆盖并发变更。</param>
/// <param name="AssignedTenantCount">当前绑定该套餐的活动租户数量。</param>
public sealed record TenantPackageSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int Version,
    int AssignedTenantCount = 0);

/// <summary>创建 Host 租户套餐请求。</summary>
/// <param name="Code">稳定套餐编码；须在 Host 作用域内保持唯一。</param>
/// <param name="Name">套餐显示名称。</param>
/// <param name="Description">套餐说明文本；可省略。</param>
public sealed record CreateHostTenantPackageRequest(
    string Code,
    string Name,
    string? Description);

/// <summary>更新 Host 租户套餐显示信息请求。</summary>
/// <param name="Name">更新后的套餐显示名称。</param>
/// <param name="Description">更新后的套餐说明文本；可省略。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record UpdateHostTenantPackageRequest(
    string Name,
    string? Description,
    int Version);
