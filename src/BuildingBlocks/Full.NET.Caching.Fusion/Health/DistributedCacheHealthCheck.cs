using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Full.NET.Caching.Fusion.Health;

/// <summary>
/// 探测受管理 L2 并核对实际 Backplane 证据；未知为降级，连续失败两次才 Unhealthy。
/// </summary>
internal sealed class DistributedCacheHealthCheck(
    IOptions<CacheOptions> cacheOptions,
    IDistributedCache distributedCache,
    CacheBackplaneHealthState backplaneState) : IHealthCheck
{
    private const string ProbeKey = "fullnet:health:ready:missing-probe";
    private const int UnhealthyFailureThreshold = 2;
    private int _consecutiveFailures;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            if (string.IsNullOrWhiteSpace(cacheOptions.Value.RedisConnectionString))
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return HealthCheckResult.Healthy();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(cacheOptions.Value.RedisOperationTimeout);
            // 使用应用自身受管理的 L2；取消探针不再遗留无人接收和释放的新建连接。
            // 此处只证明 L2 读路径，不能证明独立 Backplane 订阅已恢复。
            var probe = distributedCache.GetAsync(ProbeKey, timeout.Token);
            try
            {
                _ = await probe.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 原生命令由已配置的 Redis 超时结束；探针先返回，仍观察随后到达的故障。
                _ = probe.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw;
            }
            var backplaneStatus = backplaneState.Status;
            if (backplaneStatus == CacheBackplaneStatus.Failed)
                return Fail("缓存 Backplane 发布或订阅失败，尚未观察到对应恢复。");
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return backplaneStatus == CacheBackplaneStatus.Operational
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("缓存 Backplane 发布或订阅尚无成功证据。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("分布式缓存健康检查超时。");
        }
        catch (RedisException)
        {
            return Fail("分布式缓存健康检查失败。");
        }
        catch (InvalidOperationException)
        {
            return Fail("分布式缓存健康检查失败。");
        }
    }

    private HealthCheckResult Fail(string description)
    {
        // 单次抖动返回 Degraded（ready 仍 200）；连续失败才 Unhealthy，避免全集群同时 NotReady。
        // 检查必须以 Singleton 注册，否则 Transient 实例会丢掉滞回计数。
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        return failures >= UnhealthyFailureThreshold
            ? HealthCheckResult.Unhealthy(description)
            : HealthCheckResult.Degraded(description);
    }
}
