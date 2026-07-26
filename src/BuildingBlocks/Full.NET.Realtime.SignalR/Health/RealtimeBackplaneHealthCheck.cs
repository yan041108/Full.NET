using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Full.NET.Realtime.SignalR.Health;

internal sealed class RealtimeBackplaneHealthCheck(
    IOptions<RealtimeOptions> realtimeOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        var connectionString =
            realtimeOptions.Value.RedisBackplaneConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var configuration = ConfigurationOptions.Parse(connectionString);
            // 健康探针必须快速失败，不能沿用运行连接的后台重连语义拖住 ready。
            configuration.AbortOnConnectFail = true;
            configuration.ConnectRetry = 0;
            configuration.ConnectTimeout = 1000;
            configuration.AsyncTimeout = 1000;
            await using var connection = await ConnectionMultiplexer
                .ConnectAsync(configuration)
                .WaitAsync(timeout.Token);
            _ = await connection
                .GetDatabase()
                .PingAsync()
                .WaitAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "Realtime Redis Backplane 健康检查超时。");
        }
        catch (RedisException)
        {
            return HealthCheckResult.Unhealthy(
                "Realtime Redis Backplane 健康检查失败。");
        }
        catch (InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy(
                "Realtime Redis Backplane 健康检查失败。");
        }
    }
}
