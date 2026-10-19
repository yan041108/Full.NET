using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 监控 Error/Critical 通道队列条数与字节占用；接近满载时返回 Degraded 以提示运维扩容或降载。
/// </summary>
internal sealed class HighPriorityLoggingHealthCheck(
    FullNetLoggingMonitors monitors) : IHealthCheck
{
    private const int DegradedPercent = 90;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = monitors.HighPriority.Snapshot;
        if (monitors.HighPriority.WorkerState != 1 || monitors.HighPriority.OldestPendingSeconds > 30)
            return Task.FromResult(HealthCheckResult.Degraded("高优先级日志消费者未运行或长期没有处理进展。"));
        var byteUsage = monitors.HighPriority.ByteUsage;
        var countSaturated = snapshot.BufferSize > 0
            && (long)snapshot.Count * 100
            >= (long)snapshot.BufferSize * DegradedPercent;
        // 条数与字节任一达到 90% 即降级；高优先级通道满会导致 Error 日志被丢弃。
        var bytesSaturated = byteUsage.CapacityBytes > 0
            && byteUsage.ReservedBytes
            >= byteUsage.CapacityBytes - byteUsage.CapacityBytes / 10;
        if (countSaturated || bytesSaturated)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "高优先级日志队列条数或字节容量已达到降级阈值。"));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
