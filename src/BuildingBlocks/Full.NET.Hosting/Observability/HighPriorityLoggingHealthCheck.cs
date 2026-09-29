using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Full.NET.Hosting.Observability;

internal sealed class HighPriorityLoggingHealthCheck(
    FullNetLoggingMonitors monitors) : IHealthCheck
{
    private const int DegradedPercent = 90;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = monitors.HighPriority.Snapshot;
        var byteUsage = monitors.HighPriority.ByteUsage;
        var countSaturated = snapshot.BufferSize > 0
            && (long)snapshot.Count * 100
            >= (long)snapshot.BufferSize * DegradedPercent;
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
