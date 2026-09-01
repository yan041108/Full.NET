using Full.NET.Localization;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 租户最小只读摘要；供列表、上下文切换与开通结果展示。
/// </summary>
/// <param name="Id">租户稳定标识。</param>
/// <param name="Identifier">稳定租户编码；创建后不可变。</param>
/// <param name="Name">租户显示名称。</param>
/// <param name="Domain">租户主域名；用于解析当前请求的租户上下文。</param>
/// <param name="IsActive">是否处于活动状态；禁用租户不可登录、不可解析。</param>
/// <param name="Version">乐观并发版本；写操作须回传以避免覆盖并发变更。</param>
/// <param name="DefaultLocale">租户默认语言偏好，按 BCP 47。</param>
/// <param name="TenantPackageId">绑定的套餐标识；未绑定时为 <see langword="null"/>。</param>
/// <param name="TenantPackageCode">绑定的套餐编码；未绑定时为 <see langword="null"/>。</param>
/// <param name="TenantPackageName">绑定的套餐显示名称；未绑定时为 <see langword="null"/>。</param>
public sealed record TenantSummary(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version,
    string DefaultLocale = LocaleCatalog.DefaultLocale,
    Guid? TenantPackageId = null,
    string? TenantPackageCode = null,
    string? TenantPackageName = null);
