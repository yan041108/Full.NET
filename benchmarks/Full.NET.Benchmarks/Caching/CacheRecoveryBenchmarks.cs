using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Benchmarks.Caching;

/// <summary>
/// 缓存恢复与防击穿基准。输出低基数计数器，不宣称 10K 容量达标（Capacity-not-verified）。
/// </summary>
[MemoryDiagnoser]
public class CacheRecoveryBenchmarks
{
    private ServiceProvider _provider = null!;
    private IFusionCache _cache = null!;
    private FusionCacheEntryOptions _options = null!;
    private long _l1Hits;
    private long _l2Hits;
    private long _factoryCalls;
    private long _mergedWaiters;
    private double _invalidationMs;
    private double _staleWindowMs;
    private long _redisFailureDbAmplification;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Benchmark");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<IFusionCache>();
        _options = _provider.GetRequiredService<ICachePolicyRegistry>()
            .CreateEntryOptions(CacheEntryNames.TenantResolution);
        _options.Duration = TimeSpan.FromSeconds(30);
        _options.MemoryCacheDuration = TimeSpan.FromSeconds(30);

        _cache.Events.Memory.Hit += (_, _) => Interlocked.Increment(ref _l1Hits);
        _cache.Events.Distributed.Hit += (_, _) => Interlocked.Increment(ref _l2Hits);
        _cache.Events.Memory.Miss += (_, _) => Interlocked.Increment(ref _mergedWaiters);
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark]
    public async Task GetOrSet_coalesced_factory()
    {
        ResetCounters();
        var key = $"bench-merge-{Guid.NewGuid():N}";
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => _cache.GetOrSetAsync<string>(
                key,
                async (_, _) =>
                {
                    Interlocked.Increment(ref _factoryCalls);
                    await Task.Delay(5);
                    return "value";
                },
                _options).AsTask())
            .ToArray();
        await Task.WhenAll(tasks);
        // Miss 事件近似表示等待合并的旁路流量；真实容量需专用环境认证。
        _ = _l1Hits;
        _ = _l2Hits;
        _ = _factoryCalls;
        _ = _mergedWaiters;
    }

    [Benchmark]
    public async Task Invalidate_and_observe_stale_window()
    {
        ResetCounters();
        var key = $"bench-inv-{Guid.NewGuid():N}";
        await _cache.SetAsync(key, "stale", _options);
        var started = Stopwatch.GetTimestamp();
        await _cache.RemoveAsync(key, _options);
        _invalidationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var staleStarted = Stopwatch.GetTimestamp();
        while ((await _cache.TryGetAsync<string>(key, _options)).HasValue
            && Stopwatch.GetElapsedTime(staleStarted) < TimeSpan.FromSeconds(1))
        {
            await Task.Delay(1);
        }

        _staleWindowMs = Stopwatch.GetElapsedTime(staleStarted).TotalMilliseconds;
        _ = _invalidationMs;
        _ = _staleWindowMs;
    }

    [Benchmark]
    public async Task Redis_unavailable_amplifies_factory_to_authority()
    {
        ResetCounters();
        // 无 Redis 的基准宿主：每次 miss 都回源，用于观察失败放大上界，不声明生产容量。
        for (var i = 0; i < 32; i++)
        {
            var key = $"bench-auth-{i}-{Guid.NewGuid():N}";
            await _cache.GetOrSetAsync<string>(
                key,
                async (_, _) =>
                {
                    Interlocked.Increment(ref _factoryCalls);
                    Interlocked.Increment(ref _redisFailureDbAmplification);
                    return "authority";
                },
                _options);
        }

        _ = _redisFailureDbAmplification;
    }

    [IterationCleanup]
    public void EmitLowCardinalityCounters()
    {
        // BenchmarkDotNet 日志侧可观察这些本地计数；标签保持低基数常量。
        Console.WriteLine(
            $"cache_recovery l1_hit={_l1Hits} l2_hit={_l2Hits} factory_call={_factoryCalls} "
            + $"merged_waiter={_mergedWaiters} invalidation_ms={_invalidationMs:F3} "
            + $"stale_window_ms={_staleWindowMs:F3} redis_failure_db_amplification={_redisFailureDbAmplification}");
    }

    private void ResetCounters()
    {
        Interlocked.Exchange(ref _l1Hits, 0);
        Interlocked.Exchange(ref _l2Hits, 0);
        Interlocked.Exchange(ref _factoryCalls, 0);
        Interlocked.Exchange(ref _mergedWaiters, 0);
        _invalidationMs = 0;
        _staleWindowMs = 0;
        Interlocked.Exchange(ref _redisFailureDbAmplification, 0);
    }
}
