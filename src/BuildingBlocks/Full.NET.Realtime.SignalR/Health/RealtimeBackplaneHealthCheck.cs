using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Realtime.SignalR.Health;

/// <summary>Redis Backplane 轻量 Ping；未配置连接串时健康，连续两次失败才 Unhealthy。</summary>
internal sealed class RealtimeBackplaneHealthCheck(
    IOptions<RealtimeOptions> realtimeOptions,
    IRealtimeBackplaneProbe backplaneProbe,
    TimeProvider timeProvider,
    ILogger<RealtimeBackplaneHealthCheck> logger) : IHealthCheck
{
    private const int UnhealthyFailureThreshold = 2;
    private static readonly TimeSpan UnavailableLogInterval = TimeSpan.FromMinutes(5);
    private readonly object _probeStateLock = new();
    private int _consecutiveFailures;
    private bool _reportedUnavailable;
    private long _lastUnavailableLogTimestamp;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        var connectionString =
            realtimeOptions.Value.RedisBackplaneConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            lock (_probeStateLock)
            {
                _consecutiveFailures = 0;
                _reportedUnavailable = false;
            }
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
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return RecordResult(
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
            return RecordResult(
                startedTimestamp,
                "timeout",
                "Realtime Redis Backplane 健康检查超时。");
        }
        catch (Exception)
        {
            // ready 边界只暴露稳定结果，避免把端点、认证信息或 Redis 内部类型写入响应。
            return RecordResult(
                startedTimestamp,
                "failure",
                "Realtime Redis Backplane 健康检查失败。");
        }

        return RecordResult(startedTimestamp, "healthy", description: null);
    }

    private HealthCheckResult RecordResult(
        long startedTimestamp,
        string outcome,
        string? description)
    {
        var healthy = description is null;
        bool unavailable;
        bool writeLog = false;
        // 共享同一顺序判断就绪状态和日志边界，并限制连续失败计数，避免长时间故障回卷。
        lock (_probeStateLock)
        {
            _consecutiveFailures = healthy ? 0 : Math.Min(_consecutiveFailures + 1, UnhealthyFailureThreshold);
            unavailable = _consecutiveFailures == UnhealthyFailureThreshold;
            if (healthy)
            {
                writeLog = _reportedUnavailable;
                _reportedUnavailable = false;
            }
            else if (unavailable)
            {
                var timestamp = timeProvider.GetTimestamp();
                writeLog = !_reportedUnavailable || timeProvider.GetElapsedTime(_lastUnavailableLogTimestamp, timestamp) >= UnavailableLogInterval;
                if (writeLog)
                {
                    _reportedUnavailable = true;
                    _lastUnavailableLogTimestamp = timestamp;
                }
            }
        }

        RealtimeBackplaneTelemetry.Record(startedTimestamp, outcome, isReady: !unavailable);
        if (writeLog) LogAvailability(healthy, outcome);
        if (healthy) return HealthCheckResult.Healthy();
        return unavailable ? HealthCheckResult.Unhealthy(description) : HealthCheckResult.Degraded(description);
    }

    private void LogAvailability(bool healthy, string outcome)
    {
        try
        {
            if (healthy) RealtimeBackplaneHealthLog.Recovered(logger);
            else RealtimeBackplaneHealthLog.Unavailable(logger, outcome);
        }
        catch (Exception)
        {
            // 日志出口异常不能改变 Redis 探测结果和 HTTP 就绪状态。
        }
    }
}

internal static partial class RealtimeBackplaneHealthLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Realtime Redis backplane unavailable ({Outcome})")]
    internal static partial void Unavailable(ILogger logger, string outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Realtime Redis backplane recovered")]
    internal static partial void Recovered(ILogger logger);
}
