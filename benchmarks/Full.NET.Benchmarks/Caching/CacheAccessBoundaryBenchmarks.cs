using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Benchmarks.Caching;

/// <summary>
/// 比较业务代码直用 HybridCache 与自有缓存契约适配器的纯边界成本；
/// 只测 L1 命中和同键覆盖，不包含 Redis、序列化或权威源 I/O。
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
public class CacheAccessBoundaryBenchmarks
{
    private const int OperationsPerBatch = 10_000;
    private const string EntryName = CacheEntryNames.GridPreference;
    private const string DirectGetKey = "benchmark:cache-boundary:direct:get";
    private const string AdapterGetKey = "benchmark:cache-boundary:adapter:get";
    private const string DirectSetKey = "benchmark:cache-boundary:direct:set";
    private const string AdapterSetKey = "benchmark:cache-boundary:adapter:set";

    private readonly CachePayload _payload = new(42, "stable-payload");
    private ServiceProvider _provider = null!;
    private HybridCache _cache = null!;
    private ICachePolicyRegistry _policies = null!;
    private ICacheStorePrototype _adapter = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Benchmark");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<HybridCache>();
        _policies = _provider.GetRequiredService<ICachePolicyRegistry>();
        _adapter = new HybridCacheStorePrototype(_cache, _policies);

        await _cache.SetAsync(
            DirectGetKey,
            _payload,
            _policies.CreateHybridEntryOptions(EntryName));
        await _cache.SetAsync(
            AdapterGetKey,
            _payload,
            _policies.CreateHybridEntryOptions(EntryName));
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerBatch)]
    [BenchmarkCategory("GetL1Hit")]
    public async Task<int> DirectGetL1Hit()
    {
        var checksum = 0;
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            var payload = await _cache.GetOrCreateAsync<int, CachePayload>(
                DirectGetKey,
                index,
                static (_, _) => ValueTask.FromResult(new CachePayload(-1, "factory")),
                _policies.CreateHybridEntryOptions(EntryName));
            checksum += payload.Id;
        }

        return checksum;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerBatch)]
    [BenchmarkCategory("GetL1Hit")]
    public async Task<int> AdapterGetL1Hit()
    {
        var checksum = 0;
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            var payload = await _adapter.GetOrCreateAsync<int, CachePayload>(
                AdapterGetKey,
                index,
                static (_, _) => ValueTask.FromResult(new CachePayload(-1, "factory")),
                EntryName);
            checksum += payload.Id;
        }

        return checksum;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerBatch)]
    [BenchmarkCategory("SetL1")]
    public async Task<int> DirectSetL1()
    {
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            await _cache.SetAsync(
                DirectSetKey,
                _payload,
                _policies.CreateHybridEntryOptions(EntryName));
        }

        return _payload.Id;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerBatch)]
    [BenchmarkCategory("SetL1")]
    public async Task<int> AdapterSetL1()
    {
        for (var index = 0; index < OperationsPerBatch; index++)
        {
            await _adapter.SetAsync(
                AdapterSetKey,
                _payload,
                EntryName);
        }

        return _payload.Id;
    }

    private sealed record CachePayload(int Id, string Value);

    private interface ICacheStorePrototype
    {
        ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            string entryName,
            CancellationToken cancellationToken = default);

        ValueTask SetAsync<T>(
            string key,
            T value,
            string entryName,
            CancellationToken cancellationToken = default);
    }

    private sealed class HybridCacheStorePrototype(
        HybridCache cache,
        ICachePolicyRegistry policies) : ICacheStorePrototype
    {
        public ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            string entryName,
            CancellationToken cancellationToken = default) =>
            cache.GetOrCreateAsync(
                key,
                state,
                factory,
                policies.CreateHybridEntryOptions(entryName),
                tags: null,
                cancellationToken);

        public ValueTask SetAsync<T>(
            string key,
            T value,
            string entryName,
            CancellationToken cancellationToken = default) =>
            cache.SetAsync(
                key,
                value,
                policies.CreateHybridEntryOptions(entryName),
                tags: null,
                cancellationToken);
    }
}
