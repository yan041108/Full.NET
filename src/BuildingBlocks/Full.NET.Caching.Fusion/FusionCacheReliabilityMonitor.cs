using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace Full.NET.Caching.Fusion;

/// <summary>把 FusionCache 运行事件桥接为 Full.NET 的低基数可靠性指标。</summary>
internal sealed class FusionCacheReliabilityMonitor(
    IFusionCache cache, CacheRuntimeObservationState? observation = null) : IHostedService
{
    private int _started;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            cache.Events.Hit += HandleHit;
            cache.Events.Memory.Hit += HandleMemoryHit;
            cache.Events.Memory.Miss += HandleMemoryMiss;
            cache.Events.Memory.Eviction += HandleMemoryEviction;
            cache.Events.Distributed.Hit += HandleDistributedHit;
            cache.Events.Distributed.Miss += HandleDistributedMiss;
            cache.Events.FactorySuccess += HandleFactorySuccess;
            cache.Events.FactoryError += HandleFactoryError;
            cache.Events.FactorySyntheticTimeout += HandleFactoryTimeout;
            cache.Events.BackgroundFactorySuccess += HandleBackgroundFactorySuccess;
            cache.Events.BackgroundFactoryError += HandleBackgroundFactoryError;
            cache.Events.FailSafeActivate += HandleFailSafe;
            cache.Events.Distributed.CircuitBreakerChange += HandleDistributedCircuitBreakerChange;
            cache.Events.Backplane.CircuitBreakerChange +=
                HandleBackplaneCircuitBreakerChange;
            observation?.SetMonitoring(true);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            cache.Events.Hit -= HandleHit;
            cache.Events.Memory.Hit -= HandleMemoryHit;
            cache.Events.Memory.Miss -= HandleMemoryMiss;
            cache.Events.Memory.Eviction -= HandleMemoryEviction;
            cache.Events.Distributed.Hit -= HandleDistributedHit;
            cache.Events.Distributed.Miss -= HandleDistributedMiss;
            cache.Events.FactorySuccess -= HandleFactorySuccess;
            cache.Events.FactoryError -= HandleFactoryError;
            cache.Events.FactorySyntheticTimeout -= HandleFactoryTimeout;
            cache.Events.BackgroundFactorySuccess -= HandleBackgroundFactorySuccess;
            cache.Events.BackgroundFactoryError -= HandleBackgroundFactoryError;
            cache.Events.FailSafeActivate -= HandleFailSafe;
            cache.Events.Distributed.CircuitBreakerChange -= HandleDistributedCircuitBreakerChange;
            cache.Events.Backplane.CircuitBreakerChange -=
                HandleBackplaneCircuitBreakerChange;
            observation?.SetMonitoring(false);
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

    // 不记录 SDK Key 或异常文本；指标只描述节点实际事件，不推断业务版本陈旧。
    private void HandleMemoryHit(object? sender, FusionCacheEntryHitEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("l1", "hit", args.IsStale ? "stale" : "fresh");
    private void HandleMemoryMiss(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("l1", "miss", "miss");
    private void HandleMemoryEviction(object? sender, FusionCacheEntryEvictionEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("l1", "eviction", args.Reason switch
        {
            Microsoft.Extensions.Caching.Memory.EvictionReason.Capacity => "capacity",
            Microsoft.Extensions.Caching.Memory.EvictionReason.Expired => "expired",
            Microsoft.Extensions.Caching.Memory.EvictionReason.Removed => "removed",
            Microsoft.Extensions.Caching.Memory.EvictionReason.Replaced => "replaced",
            Microsoft.Extensions.Caching.Memory.EvictionReason.TokenExpired => "token_expired",
            _ => "unknown",
        });
    private void HandleDistributedHit(object? sender, FusionCacheEntryHitEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("l2", "hit", args.IsStale ? "stale" : "fresh");
    private void HandleDistributedMiss(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("l2", "miss", "miss");
    private void HandleFactorySuccess(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("factory", "foreground", "success");
    private void HandleFactoryError(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("factory", "foreground", "failure");
    private void HandleFactoryTimeout(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("factory", "foreground", "timeout");
    private void HandleBackgroundFactorySuccess(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("factory", "background", "success");
    private void HandleBackgroundFactoryError(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("factory", "background", "failure");
    private void HandleFailSafe(object? sender, FusionCacheEntryEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("cache", "fail_safe", "activated");
    private void HandleDistributedCircuitBreakerChange(object? sender, FusionCacheCircuitBreakerChangeEventArgs args) =>
        CacheReliabilityTelemetry.RecordRuntimeEvent("l2", "circuit", args.IsClosed ? "closed" : "open");
}
