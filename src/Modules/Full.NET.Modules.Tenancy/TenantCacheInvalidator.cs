using System.Diagnostics;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 集中执行租户解析缓存失效。提交后路径先清当前实例 L1，再同步尽力删除 L2 并广播 Backplane；
/// 兼容 Handler 仍可调用分布式失效以排空存量 Outbox。
/// </summary>
internal sealed class TenantCacheInvalidator(
    IFusionCache cache,
    IHostEnvironment environment,
    ICachePolicyRegistry policies,
    ILogger<TenantCacheInvalidator> logger)
{
    /// <summary>
    /// 事务提交成功后直接失效当前实例 L1、共享 L2，并同步广播 Backplane。
    /// 必须先清 L1，保证 Redis 不可达时本节点仍立即收敛；L2/Backplane 失败只告警，不得补写 Outbox。
    /// </summary>
    public async Task InvalidateAfterCommitAsync(
        Guid tenantId,
        string domain,
        CancellationToken cancellationToken)
    {
        try
        {
            // 先只清本节点 L1，避免 L2/Backplane 异常阻断本地负缓存修复。
            await InvalidateAsync(
                    tenantId,
                    domain,
                    CreateLocalOptions(),
                    distributed: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "租户解析缓存提交后本地 L1 失效失败；依赖 TTL/版本/权威源收敛。TenantId={TenantId} Domain={Domain}",
                tenantId,
                domain);
            return;
        }

        try
        {
            await InvalidateAsync(
                    tenantId,
                    domain,
                    CreateAfterCommitDistributedOptions(),
                    distributed: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "租户解析缓存提交后 L2/Backplane 失效失败；本节点 L1 已清理，依赖 TTL/版本/权威源与后续广播收敛。TenantId={TenantId} Domain={Domain}",
                tenantId,
                domain);
        }
    }

    /// <summary>
    /// 兼容存量 Outbox 排空：必须等待 L2/Backplane 完成，异常向上抛出让 Outbox 重试。
    /// </summary>
    public Task InvalidateDistributedAsync(
        Guid tenantId,
        string domain,
        CancellationToken cancellationToken) =>
        InvalidateAsync(
            tenantId,
            domain,
            CreateAfterCommitDistributedOptions(),
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
            distributed ? "invalidate_after_commit" : "invalidate_local",
            succeeded ? "success" : "failure");
    }

    private FusionCacheEntryOptions CreateLocalOptions()
    {
        var options = policies.CreateEntryOptions(CacheEntryNames.TenantResolution);
        // 仅修复当前实例内存缓存，不依赖 Redis 可用性。
        options.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        return options;
    }

    private FusionCacheEntryOptions CreateAfterCommitDistributedOptions()
    {
        var options = policies.CreateEntryOptions(CacheEntryNames.TenantResolution);
        // 提交后路径与兼容 Handler 都必须同步等待 L2 删除与 Backplane 广播。
        options.AllowBackgroundDistributedCacheOperations = false;
        options.ReThrowDistributedCacheExceptions = true;
        options.AllowBackgroundBackplaneOperations = false;
        options.ReThrowBackplaneExceptions = true;
        return options;
    }
}
