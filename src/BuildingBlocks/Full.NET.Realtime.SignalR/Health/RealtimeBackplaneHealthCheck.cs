using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Realtime.SignalR.Health;

internal sealed class RealtimeBackplaneHealthCheck(
    IOptions<RealtimeOptions> realtimeOptions,
    IRealtimeBackplaneProbe backplaneProbe) : IHealthCheck
{
    private const int UnhealthyFailureThreshold = 2;
    private int _consecutiveFailures;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        var connectionString =
            realtimeOptions.Value.RedisBackplaneConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return HealthCheckResult.Healthy();
        }

        var startedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await backplaneProbe.PingAsync(
                connectionString,
                timeout.Token);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            RealtimeBackplaneTelemetry.Record(
                startedTimestamp,
                "healthy",
                isReady: true);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(
                startedTimestamp,
                "timeout",
                "Realtime Redis Backplane 健康检查超时。");
        }
        catch (OperationCanceledException)
        {
            // 调用方取消不是 Backplane 故障，必须继续传播给 Health Check 管道。
            throw;
        }
        catch (TimeoutException)
        {
            // Redis 原生超时与本地两秒预算属于同一容量/网络故障分类，且不得暴露端点详情。
            return Fail(
                startedTimestamp,
                "timeout",
                "Realtime Redis Backplane 健康检查超时。");
        }
        catch (Exception)
        {
            // ready 边界只暴露稳定结果，避免把端点、认证信息或 Redis 内部类型写入响应。
            return Fail(
                startedTimestamp,
                "failure",
                "Realtime Redis Backplane 健康检查失败。");
        }
    }

    private HealthCheckResult Fail(
        long startedTimestamp,
        string outcome,
        string description)
    {
        // 检查必须以 Singleton 注册，否则 Transient 实例会丢掉滞回计数。
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        var unhealthy = failures >= UnhealthyFailureThreshold;
        RealtimeBackplaneTelemetry.Record(
            startedTimestamp,
            outcome,
            isReady: !unhealthy);
        return unhealthy
            ? HealthCheckResult.Unhealthy(description)
            : HealthCheckResult.Degraded(description);
    }
}
