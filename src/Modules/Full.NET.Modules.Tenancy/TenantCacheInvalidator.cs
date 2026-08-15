using System.Diagnostics;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 集中执行租户解析缓存失效。两级失效策略：
/// 提交后路径先清当前实例 L1（内存），再同步尽力删除 L2 Redis 并广播 Backplane；
/// 兼容存量 Outbox Handler 仍可调用 InvalidateDistributedAsync 仅排空 L2/Backplane。
/// 缓存键由 EnvironmentName 前缀隔离，所以失效同样按环境范围，不跨环境串扰。
/// </summary>
internal sealed class TenantCacheInvalidator(
    IFusionCache cache,
    IHostEnvironment environment,
    ICachePolicyRegistry policies,
    ILogger<TenantCacheInvalidator> logger)
{
    /// <summary>
    /// 事务提交成功后立即执行的缓存失效路径。顺序：
    /// 1) 先同步清本节点 L1（SkipDistributedCache=true），Redis 异常时本节点先收敛；
    /// 2) 再同步删 L2 + 广播 Backplane，失败只告警不抛错，不得重新写入缓存专用 Outbox；
    /// 3) 每一步都记录 CacheReliabilityTelemetry，便于监控 L1/L2 成功率。
    /// </summary>
    /// <param name="tenantId">发生开通/变更/禁用的租户 ID。</param>
    /// <param name="domain">租户绑定域名，用于按域解析缓存键和 DomainTag 失效。</param>
    /// <param name="cancellationToken">失效取消；由调用方在提交后路径通常传 CancellationToken.None。</param>
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
    /// 兼容存量 Outbox 排空：只操作 L2 Redis 并广播 Backplane。
    /// 与 InvalidateAfterCommitAsync 不同，此路径异常会向上抛出，让 Outbox 调度器按指数退避重试，
    /// 直到共享缓存收敛；要求事件幂等，重复删除等价于同一缓存缺失状态。
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
