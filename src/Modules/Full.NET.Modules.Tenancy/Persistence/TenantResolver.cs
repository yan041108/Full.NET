using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using global::Dapper;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// 基于 HybridCache 的租户解析实现。缓存策略：
/// - ById / ByDomain 都以 CacheEntryNames.TenantResolution 策略做两级缓存；
/// - DB 未命中首次写入 Negative 短 TTL 的空占位，避免缓存穿透；命中且活动后
///   升级为正常 TTL 并打 TenantTag + DomainTag 以便变更时按标签批量失效；
/// - L2 Redis 不可达时回落到数据库查询 + L1，允许降级但延迟会上升。
/// 解析不变量：返回 IsActive=false 的租户等价于未找到，调用方不得将其注入上下文。
/// </summary>
internal sealed class TenantResolver(
    IQueryExecutor queryExecutor,
    HybridCache cache,
    ICachePolicyRegistry policies,
    IHostEnvironment environment) : ITenantResolver, IActiveTenantContextResolver
{
    /// <summary>
    /// 按域名解析租户；先做域名归一化（去尾部点+小写），再按 EnvironmentName 隔离缓存键，
    /// 避免开发/预发共用同一 Redis 时键值串扰。
    /// </summary>
    public async Task<TenantSummary?> ResolveByDomainAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var cacheKey = CacheKeyBuilder.TenantResolutionByDomain(
            environment.EnvironmentName,
            normalizedDomain);
        var domainTag = CacheKeyBuilder.DomainTag(normalizedDomain);
        var loaded = false;

        var entry = await cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    loaded = true;
                    var tenant = await queryExecutor
                        .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                            TenantSql.FindByDomain,
                            CreateDomainParameters(normalizedDomain),
                            token)
                        .ConfigureAwait(false);
                    return TenantResolutionCacheMapper.ToCacheEntry(tenant?.ToSummary());
                },
                policies.CreateHybridEntryOptions(
                    CacheEntryNames.TenantResolution,
                    CacheEntryLifetime.Negative),
                [domainTag],
                cancellationToken)
            .ConfigureAwait(false);

        if (loaded && entry.Tenant is { IsActive: true } activePayload)
        {
            var activeTenant = TenantResolutionCacheMapper.ToTenantSummary(activePayload)!;
            await cache.SetAsync(
                    cacheKey,
                    entry,
                    policies.CreateHybridEntryOptions(CacheEntryNames.TenantResolution),
                    [domainTag, CacheKeyBuilder.TenantTag(activeTenant.Id)],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return TenantResolutionCacheMapper.ToTenantSummary(entry);
    }

    public async Task<TenantSummary?> ResolveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyBuilder.TenantResolutionById(
            environment.EnvironmentName,
            tenantId);
        var loaded = false;
        var entry = await cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    loaded = true;
                    return TenantResolutionCacheMapper.ToCacheEntry(
                        (await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                            TenantSql.FindById,
                            CreateTenantIdParameters(tenantId),
                            token)
                        .ConfigureAwait(false))?.ToSummary());
                },
                policies.CreateHybridEntryOptions(
                    CacheEntryNames.TenantResolution,
                    CacheEntryLifetime.Negative),
                [CacheKeyBuilder.TenantTag(tenantId)],
                cancellationToken)
            .ConfigureAwait(false);
        if (loaded && entry.Tenant is { IsActive: true })
        {
            await cache.SetAsync(
                    cacheKey,
                    entry,
                    policies.CreateHybridEntryOptions(CacheEntryNames.TenantResolution),
                    [CacheKeyBuilder.TenantTag(tenantId)],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return TenantResolutionCacheMapper.ToTenantSummary(entry);
    }

    public async Task<TenantContext?> ResolveActiveByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        return tenant is { IsActive: true }
            ? new TenantContext(tenant.Id, tenant.Identifier, tenant.Name)
            : null;
    }

    public async Task<IReadOnlyList<TenantSummary>> GetAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<TenantResolutionRecord>(
            TenantSql.GetAvailable,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static string NormalizeDomain(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static DynamicParameters CreateDomainParameters(string domain)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Domain", domain);
        return parameters;
    }

    private static DynamicParameters CreateTenantIdParameters(Guid tenantId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);
        return parameters;
    }
}
