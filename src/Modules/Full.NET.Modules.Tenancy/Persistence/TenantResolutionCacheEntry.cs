namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// 租户解析 HybridCache 分布式缓存载荷；<see cref="Tenant"/> 为 <see langword="null"/>
/// 时表示负缓存占位。该形状只属于 Tenancy 模块，不构成跨模块业务契约。
/// </summary>
internal sealed record TenantResolutionCacheEntry(TenantCachePayload? Tenant);

/// <summary>
/// 租户解析缓存中的稳定字段快照；属性名和类型必须保持与既有 L2 JSON 兼容。
/// </summary>
internal sealed record TenantCachePayload(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version,
    string DefaultLocale,
    Guid? TenantPackageId,
    string? TenantPackageCode,
    string? TenantPackageName);
