using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Benchmarks.Caching;

/// <summary>
/// 单进程纯内存微基准：没有 Redis、数据库或第二节点，不能证明故障恢复或容量（Capacity-not-verified）。
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
    private long _memoryMisses;
    private double _invalidationMs;
    private double _localAbsenceProbeMs;
    private long _coldFactoryCalls;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Benchmark");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<IFusionCache>();
        _options = _provider.GetRequiredService<ICachePolicyRegistry>()
            .CreateEntryOptions(CacheEntryNames.GridPreference);
        _options.Duration = TimeSpan.FromSeconds(30);
        _options.MemoryCacheDuration = TimeSpan.FromSeconds(30);

        _cache.Events.Memory.Hit += (_, _) => Interlocked.Increment(ref _l1Hits);
        _cache.Events.Distributed.Hit += (_, _) => Interlocked.Increment(ref _l2Hits);
        _cache.Events.Memory.Miss += (_, _) => Interlocked.Increment(ref _memoryMisses);
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
        // Memory Miss 是未命中事件计数，不能解释为被合并的等待者数量。
        _ = _l1Hits;
        _ = _l2Hits;
        _ = _factoryCalls;
        _ = _memoryMisses;
    }

    [Benchmark]
    public async Task Remove_local_entry_and_verify_absence()
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

        _localAbsenceProbeMs = Stopwatch.GetElapsedTime(staleStarted).TotalMilliseconds;
        _ = _invalidationMs;
        _ = _localAbsenceProbeMs;
    }

    [Benchmark]
    public async Task Local_cold_keys_execute_factories()
    {
        ResetCounters();
        // 无 Redis/数据库的宿主；每个新键执行本地字符串工厂，不能代表 Redis 故障后的数据库放大。
        for (var i = 0; i < 32; i++)
        {
            var key = $"bench-auth-{i}-{Guid.NewGuid():N}";
            await _cache.GetOrSetAsync<string>(
                key,
                async (_, _) =>
                {
                    Interlocked.Increment(ref _factoryCalls);
                    Interlocked.Increment(ref _coldFactoryCalls);
                    return "authority";
                },
                _options);
        }

        _ = _coldFactoryCalls;
    }

    [IterationCleanup]
    public void EmitLowCardinalityCounters()
    {
        // BenchmarkDotNet 日志侧可观察这些本地计数；标签保持低基数常量。
        Console.WriteLine(
            $"cache_recovery l1_hit={_l1Hits} l2_hit={_l2Hits} factory_call={_factoryCalls} "
            + $"memory_miss={_memoryMisses} invalidation_ms={_invalidationMs:F3} "
            + $"local_absence_probe_ms={_localAbsenceProbeMs:F3} local_cold_factory_call={_coldFactoryCalls}");
    }

    private void ResetCounters()
    {
        Interlocked.Exchange(ref _l1Hits, 0);
        Interlocked.Exchange(ref _l2Hits, 0);
        Interlocked.Exchange(ref _factoryCalls, 0);
        Interlocked.Exchange(ref _memoryMisses, 0);
        _invalidationMs = 0;
        _localAbsenceProbeMs = 0;
        Interlocked.Exchange(ref _coldFactoryCalls, 0);
    }
}
