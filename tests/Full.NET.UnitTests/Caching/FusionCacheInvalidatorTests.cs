using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

[TestClass]
public sealed class FusionCacheInvalidatorTests
{
    [TestMethod]
    public void AddFullNetCaching_registers_cache_invalidator_contract()
    {
        using var provider = CreateServices().BuildServiceProvider();

        Assert.IsNotNull(provider.GetRequiredService<ICacheInvalidator>());
    }

    [TestMethod]
    public async Task CurrentNodeOnly_removes_l1_without_calling_failing_l2()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var fusionCache = provider.GetRequiredService<IFusionCache>();
        var invalidator = provider.GetRequiredService<ICacheInvalidator>();
        const string key = "fullnet:testing:host:settings:grid-preference:test:v1";
        await hybridCache.SetAsync(key, "stale");
        fusionCache.SetupDistributedCache(new ThrowingDistributedCache());

        await invalidator.RemoveAsync(
            CacheEntryNames.GridPreference,
            key,
            CacheInvalidationScope.CurrentNodeOnly,
            CancellationToken.None);

        var actual = await hybridCache.GetOrCreateAsync(
            key,
            _ => ValueTask.FromResult("fresh"));
        Assert.AreEqual("fresh", actual);
    }

    [TestMethod]
    public async Task AllLayersSynchronous_propagates_distributed_cache_failure()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        var fusionCache = provider.GetRequiredService<IFusionCache>();
        fusionCache.SetupDistributedCache(new ThrowingDistributedCache());
        var invalidator = provider.GetRequiredService<ICacheInvalidator>();

        await Assert.ThrowsExactlyAsync<FusionCacheDistributedCacheException>(
            async () => await invalidator.RemoveAsync(
                CacheEntryNames.GridPreference,
                "fullnet:testing:host:settings:grid-preference:test:v1",
                CacheInvalidationScope.AllLayersSynchronous,
                CancellationToken.None));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Testing");
        return services;
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) =>
            throw new InvalidOperationException("模拟 L2 缓存读取失败。");

        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException<byte[]?>(new InvalidOperationException("模拟 L2 缓存读取失败。"));

        public void Refresh(string key) =>
            throw new InvalidOperationException("模拟 L2 缓存刷新失败。");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("模拟 L2 缓存刷新失败。"));

        public void Remove(string key) =>
            throw new InvalidOperationException("模拟 L2 缓存删除失败。");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("模拟 L2 缓存删除失败。"));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("模拟 L2 缓存写入失败。");

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("模拟 L2 缓存写入失败。"));
    }
}
