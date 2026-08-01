using System.Diagnostics;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy;

/// <summary>集中执行租户解析缓存的本地修复与可靠跨节点失效。</summary>
internal sealed class TenantCacheInvalidator(
    IFusionCache cache,
    IHostEnvironment environment,
    ICachePolicyRegistry policies)
{
    public Task InvalidateLocalAsync(Guid tenantId, string domain) =>
        InvalidateAsync(
            tenantId,
            domain,
            CreateLocalOptions(),
            distributed: false,
            CancellationToken.None);

    public Task InvalidateDistributedAsync(
        Guid tenantId,
        string domain,
        CancellationToken cancellationToken) =>
        InvalidateAsync(
            tenantId,
            domain,
            CreateDistributedOptions(),
            distributed: true,
            cancellationToken);

    private async Task InvalidateAsync(
        Guid tenantId,
        string domain,
        FusionCacheEntryOptions options,
        bool distributed,
        CancellationToken cancellationToken)
    {
        var policy = policies.GetRequired(CacheEntryNames.TenantResolution);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await cache.RemoveAsync(
                    CacheKeyBuilder.TenantResolutionById(
                        environment.EnvironmentName,
                        tenantId),
                    options,
                    token: cancellationToken)
                .ConfigureAwait(false);
            await cache.RemoveAsync(
                    CacheKeyBuilder.TenantResolutionByDomain(
                        environment.EnvironmentName,
                        domain),
                    options,
                    token: cancellationToken)
                .ConfigureAwait(false);
            await cache.RemoveByTagAsync(
                    CacheKeyBuilder.TenantTag(tenantId),
                    options,
                    token: cancellationToken)
                .ConfigureAwait(false);
            await cache.RemoveByTagAsync(
                    CacheKeyBuilder.DomainTag(domain),
                    options,
                    token: cancellationToken)
                .ConfigureAwait(false);
            RecordInvalidation(
                policy,
                distributed,
                Stopwatch.GetElapsedTime(startedAt),
                succeeded: true);
        }
        catch
        {
            RecordInvalidation(
                policy,
                distributed,
                Stopwatch.GetElapsedTime(startedAt),
                succeeded: false);
            throw;
        }
    }

    private static void RecordInvalidation(
        CacheEntryPolicy policy,
        bool distributed,
        TimeSpan duration,
        bool succeeded)
    {
        if (distributed)
        {
            CacheReliabilityTelemetry.RecordDistributedInvalidation(
                duration,
                succeeded);
        }
        else
        {
            CacheReliabilityTelemetry.RecordLocalInvalidation(
                duration,
                succeeded);
        }

        CacheReliabilityTelemetry.RecordPolicyEvent(
            policy.OwnerModule,
            policy.ConsistencyClassTag,
            distributed ? "invalidate_distributed" : "invalidate_local",
            succeeded ? "success" : "failure");
    }

    private FusionCacheEntryOptions CreateLocalOptions()
    {
        var options = policies.CreateEntryOptions(CacheEntryNames.TenantResolution);
        // API 请求只修复本节点；跨节点传播由事务 Outbox 消费者负责，避免提交语义受请求生命周期影响。
        options.SkipBackplaneNotifications = true;
        return options;
    }

    private FusionCacheEntryOptions CreateDistributedOptions()
    {
        var options = policies.CreateEntryOptions(CacheEntryNames.TenantResolution);
        // Worker 必须等待 L2 删除与广播完成并让异常进入 Outbox 重试，不能把未传播的失效误记为已完成。
        options.AllowBackgroundDistributedCacheOperations = false;
        options.ReThrowDistributedCacheExceptions = true;
        options.AllowBackgroundBackplaneOperations = false;
        options.ReThrowBackplaneExceptions = true;
        return options;
    }
}
