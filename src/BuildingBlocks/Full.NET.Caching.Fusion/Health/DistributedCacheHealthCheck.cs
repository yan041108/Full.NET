using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Full.NET.Caching.Fusion.Health;

internal sealed class DistributedCacheHealthCheck(
    IOptions<CacheOptions> cacheOptions) : IHealthCheck
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
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var configuration = ConfigurationOptions.Parse(
                cacheOptions.Value.RedisConnectionString);
            configuration.AbortOnConnectFail = true;
            configuration.ConnectRetry = 0;
            configuration.ConnectTimeout = 1000;
            configuration.AsyncTimeout = 1000;
            await using var connection = await ConnectionMultiplexer.ConnectAsync(
                configuration).WaitAsync(timeout.Token);
            if (!connection.IsConnected)
            {
                return Fail("分布式缓存健康检查失败。");
            }

            _ = await connection.GetDatabase().StringGetAsync(ProbeKey).WaitAsync(timeout.Token);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return HealthCheckResult.Healthy();
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
