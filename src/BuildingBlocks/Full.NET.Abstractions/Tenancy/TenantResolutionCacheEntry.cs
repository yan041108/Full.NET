namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 租户解析 HybridCache 分布式缓存载荷；<see cref="Tenant"/> 为 <c>null</c> 表示负缓存占位。
/// </summary>
public sealed record TenantResolutionCacheEntry(TenantCachePayload? Tenant);
