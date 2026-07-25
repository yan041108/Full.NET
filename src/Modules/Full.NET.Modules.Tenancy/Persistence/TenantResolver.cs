using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Tenancy.Persistence;

internal sealed class TenantResolver(
    IQueryExecutor queryExecutor,
    HybridCache cache,
    IHostEnvironment environment) : ITenantResolver
{
    internal static readonly TimeSpan ActiveTenantDuration = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MissingTenantDuration = TimeSpan.FromMinutes(1);

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
                            new { Domain = normalizedDomain },
                            token)
                        .ConfigureAwait(false);
                    return new CachedTenantResolution(tenant?.ToSummary());
                },
                new HybridCacheEntryOptions
                {
                    Expiration = MissingTenantDuration,
                    LocalCacheExpiration = MissingTenantDuration
                },
                [domainTag],
                cancellationToken)
            .ConfigureAwait(false);

        if (loaded && entry.Tenant is { IsActive: true } activeTenant)
        {
            await cache.SetAsync(
                    cacheKey,
                    entry,
                    new HybridCacheEntryOptions
                    {
                        Expiration = ActiveTenantDuration,
                        LocalCacheExpiration = ActiveTenantDuration
                    },
                    [domainTag, CacheKeyBuilder.TenantTag(activeTenant.Id)],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return entry.Tenant;
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
                    return new CachedTenantResolution(
                        (await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                            TenantSql.FindById,
                            new { TenantId = tenantId },
                            token)
                        .ConfigureAwait(false))?.ToSummary());
                },
                new HybridCacheEntryOptions
                {
                    Expiration = MissingTenantDuration,
                    LocalCacheExpiration = MissingTenantDuration,
                },
                [CacheKeyBuilder.TenantTag(tenantId)],
                cancellationToken)
            .ConfigureAwait(false);
        if (loaded && entry.Tenant is { IsActive: true })
        {
            await cache.SetAsync(
                    cacheKey,
                    entry,
                    new HybridCacheEntryOptions
                    {
                        Expiration = ActiveTenantDuration,
                        LocalCacheExpiration = ActiveTenantDuration,
                    },
                    [CacheKeyBuilder.TenantTag(tenantId)],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return entry.Tenant;
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

    private sealed record CachedTenantResolution(TenantSummary? Tenant);
}
