namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 租户解析 HybridCache 分布式缓存中的租户字段快照；
/// 字段与 <c>Full.NET.Modules.Tenancy.Contracts.TenantSummary</c> 对齐，供 AOT 源生成序列化闭包使用。
/// </summary>
public sealed record TenantCachePayload(
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
