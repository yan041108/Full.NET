using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// 租户解析缓存载荷与模块契约 <see cref="TenantSummary"/> 的双向映射。
/// </summary>
internal static class TenantResolutionCacheMapper
{
    public static TenantResolutionCacheEntry ToCacheEntry(TenantSummary? tenant) =>
        new(tenant is null ? null : ToPayload(tenant));

    public static TenantSummary? ToTenantSummary(TenantResolutionCacheEntry entry) =>
        entry.Tenant is null ? null : ToSummary(entry.Tenant);

    public static TenantSummary ToTenantSummary(TenantCachePayload payload) =>
        ToSummary(payload);

    private static TenantCachePayload ToPayload(TenantSummary tenant) =>
        new(
            tenant.Id,
            tenant.Identifier,
            tenant.Name,
            tenant.Domain,
            tenant.IsActive,
            tenant.Version,
            tenant.DefaultLocale,
            tenant.TenantPackageId,
            tenant.TenantPackageCode,
            tenant.TenantPackageName);

    private static TenantSummary ToSummary(TenantCachePayload payload) =>
        new(
            payload.Id,
            payload.Identifier,
            payload.Name,
            payload.Domain,
            payload.IsActive,
            payload.Version,
            payload.DefaultLocale,
            payload.TenantPackageId,
            payload.TenantPackageCode,
            payload.TenantPackageName);
}
