using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace Full.NET.Caching.Fusion;

/// <summary>把 FusionCache 运行事件桥接为 Full.NET 的低基数可靠性指标。</summary>
internal sealed class FusionCacheReliabilityMonitor(
    IFusionCache cache) : IHostedService
{
    private int _started;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            cache.Events.Hit += HandleHit;
            cache.Events.Backplane.CircuitBreakerChange +=
                HandleBackplaneCircuitBreakerChange;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            cache.Events.Hit -= HandleHit;
            cache.Events.Backplane.CircuitBreakerChange -=
                HandleBackplaneCircuitBreakerChange;
        }

        return Task.CompletedTask;
    }

    internal void HandleHit(
        object? sender,
        FusionCacheEntryHitEventArgs args)
    {
        if (args.IsStale)
        {
            CacheReliabilityTelemetry.RecordStaleHit();
        }
    }

    internal void HandleBackplaneCircuitBreakerChange(
        object? sender,
        FusionCacheCircuitBreakerChangeEventArgs args) =>
        CacheReliabilityTelemetry.RecordBackplaneCircuitTransition(
            args.IsClosed);
}
