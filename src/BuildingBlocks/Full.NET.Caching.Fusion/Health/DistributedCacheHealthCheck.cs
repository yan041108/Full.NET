using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Full.NET.Caching.Fusion.Health;

internal sealed class DistributedCacheHealthCheck(
    IOptions<CacheOptions> cacheOptions) : IHealthCheck
{
    private const string ProbeKey = "fullnet:health:ready:missing-probe";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            if (string.IsNullOrWhiteSpace(cacheOptions.Value.RedisConnectionString))
            {
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
                return HealthCheckResult.Unhealthy("分布式缓存健康检查失败。");
            }

            _ = await connection.GetDatabase().StringGetAsync(ProbeKey).WaitAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("分布式缓存健康检查超时。");
        }
        catch (RedisException)
        {
            return HealthCheckResult.Unhealthy("分布式缓存健康检查失败。");
        }
        catch (InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("分布式缓存健康检查失败。");
        }
    }
}
