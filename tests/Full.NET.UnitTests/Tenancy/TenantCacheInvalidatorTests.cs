using System.Diagnostics.Metrics;
using Full.NET.Caching.Fusion;
using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
[DoNotParallelize]
public sealed class TenantCacheInvalidatorTests
{
    [TestMethod]
    public async Task InvalidateAfterCommitAsync_RemovesLocalEntries_AndRecordsSuccess()
    {
        var measurements = new List<InvalidationMeasurement>();
        using var listener = CreateInvalidationListener(measurements);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        await using var provider = services.BuildServiceProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var fusionCache = provider.GetRequiredService<IFusionCache>();
        const string environmentName = "Testing";
        var tenantId = Guid.CreateVersion7();
        const string domain = "direct.localhost";
        var tenantKey = CacheKeyBuilder.TenantResolutionById(environmentName, tenantId);
        var domainKey = CacheKeyBuilder.TenantResolutionByDomain(environmentName, domain);
        await hybridCache.SetAsync(tenantKey, "stale-tenant");
        await hybridCache.SetAsync(domainKey, "stale-domain");

        var invalidator = new TenantCacheInvalidator(
            fusionCache,
            new TestHostEnvironment(environmentName),
            CachePolicyRegistry.Create(new CacheOptions()),
            NullLogger<TenantCacheInvalidator>.Instance);

        await invalidator.InvalidateAfterCommitAsync(
            tenantId,
            domain,
            CancellationToken.None);

        Assert.AreEqual(
            "fresh-tenant",
            await hybridCache.GetOrCreateAsync(
                tenantKey,
                _ => ValueTask.FromResult("fresh-tenant")));
        Assert.AreEqual(
            "fresh-domain",
            await hybridCache.GetOrCreateAsync(
                domainKey,
                _ => ValueTask.FromResult("fresh-domain")));
        var distributedSuccess = measurements.Single(item =>
            item.Name == "fullnet.cache.invalidation.duration"
            && item.Tags.Any(tag =>
                tag.Key == "scope"
                && Equals(tag.Value, "distributed")));
        Assert.AreEqual(
            "success",
            distributedSuccess.Tags.Single(tag => tag.Key == "outcome").Value);
    }

    [TestMethod]
    public async Task InvalidateAfterCommitAsync_ClearsLocalEvenWhenDistributedFails()
    {
        var measurements = new List<InvalidationMeasurement>();
        using var listener = CreateInvalidationListener(measurements);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache()
            .WithSystemTextJsonSerializer()
            .AsHybridCache();
        await using var provider = services.BuildServiceProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var cache = provider.GetRequiredService<IFusionCache>();
        cache.SetupDistributedCache(new ThrowingDistributedCache());
        const string environmentName = "Testing";
        var tenantId = Guid.CreateVersion7();
        const string domain = "fail.localhost";
        var tenantKey = CacheKeyBuilder.TenantResolutionById(environmentName, tenantId);
        var domainKey = CacheKeyBuilder.TenantResolutionByDomain(environmentName, domain);
        await hybridCache.SetAsync(tenantKey, "stale-tenant");
        await hybridCache.SetAsync(domainKey, "stale-domain");
        var invalidator = new TenantCacheInvalidator(
            cache,
            new TestHostEnvironment(environmentName),
            CachePolicyRegistry.Create(new CacheOptions()),
            NullLogger<TenantCacheInvalidator>.Instance);

        await invalidator.InvalidateAfterCommitAsync(
            tenantId,
            domain,
            CancellationToken.None);

        Assert.AreEqual(
            "fresh-tenant",
            await hybridCache.GetOrCreateAsync(
                tenantKey,
                _ => ValueTask.FromResult("fresh-tenant")));
        Assert.AreEqual(
            "fresh-domain",
            await hybridCache.GetOrCreateAsync(
                domainKey,
                _ => ValueTask.FromResult("fresh-domain")));
        AssertFailedDistributedInvalidation(measurements);
        Assert.IsTrue(
            measurements.Any(item =>
                item.Name == "fullnet.cache.invalidation.duration"
                && item.Tags.Any(tag =>
                    tag.Key == "scope"
                    && Equals(tag.Value, "local"))
                && item.Tags.Any(tag =>
                    tag.Key == "outcome"
                    && Equals(tag.Value, "success"))));
    }

    [TestMethod]
    public async Task InvalidateDistributedAsync_PropagatesFailures_ForCompatDrain()
    {
        var measurements = new List<InvalidationMeasurement>();
        using var listener = CreateInvalidationListener(measurements);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IFusionCache>();
        cache.SetupBackplane(new ThrowingBackplane());
        var invalidator = new TenantCacheInvalidator(
            cache,
            new TestHostEnvironment("Testing"),
            CachePolicyRegistry.Create(new CacheOptions()),
            NullLogger<TenantCacheInvalidator>.Instance);

        await Assert.ThrowsExactlyAsync<FusionCacheBackplaneException>(
            () => invalidator.InvalidateDistributedAsync(
                Guid.CreateVersion7(),
                "compat.localhost",
                CancellationToken.None));
        AssertFailedDistributedInvalidation(measurements);
    }

    private static void AssertFailedDistributedInvalidation(
        List<InvalidationMeasurement> measurements)
    {
        var distributedFailure = measurements.Single(item =>
            item.Name == "fullnet.cache.invalidation.duration"
            && item.Tags.Any(tag =>
                tag.Key == "scope"
                && Equals(tag.Value, "distributed")));
        Assert.AreEqual(
            "failure",
            distributedFailure.Tags
                .Single(tag => tag.Key == "outcome")
                .Value);
        Assert.IsTrue(measurements.Any(item =>
            item.Name == "fullnet.cache.invalidation.failures"
            && item.Tags.Any(tag =>
                tag.Key == "scope"
                && Equals(tag.Value, "distributed"))));
    }

    private static MeterListener CreateInvalidationListener(
        List<InvalidationMeasurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == CacheReliabilityTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
                measurements.Add(
                    new InvalidationMeasurement(
                        instrument.Name,
                        value,
                        tags.ToArray())));
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
                measurements.Add(
                    new InvalidationMeasurement(
                        instrument.Name,
                        value,
                        tags.ToArray())));
        listener.Start();
        return listener;
    }

    private sealed record InvalidationMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Full.NET.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class ThrowingBackplane : IFusionCacheBackplane
    {
        public void Subscribe(BackplaneSubscriptionOptions options)
        {
        }

        public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) =>
            ValueTask.CompletedTask;

        public void Unsubscribe()
        {
        }

        public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;

        public void Publish(
            BackplaneMessage message,
            FusionCacheEntryOptions options,
            CancellationToken token = default) =>
            throw new InvalidOperationException("模拟 Backplane 发布失败。");

        public ValueTask PublishAsync(
            BackplaneMessage message,
            FusionCacheEntryOptions options,
            CancellationToken token = default) =>
            ValueTask.FromException(
                new InvalidOperationException("模拟 Backplane 发布失败。"));

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) =>
            throw new InvalidOperationException("模拟 L2 缓存读取失败。");

        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException<byte[]?>(
                new InvalidOperationException("模拟 L2 缓存读取失败。"));

        public void Refresh(string key) =>
            throw new InvalidOperationException("模拟 L2 缓存刷新失败。");

        public Task RefreshAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException(
                new InvalidOperationException("模拟 L2 缓存刷新失败。"));

        public void Remove(string key) =>
            throw new InvalidOperationException("模拟 L2 缓存删除失败。");

        public Task RemoveAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException(
                new InvalidOperationException("模拟 L2 缓存删除失败。"));

        public void Set(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("模拟 L2 缓存写入失败。");

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            Task.FromException(
                new InvalidOperationException("模拟 L2 缓存写入失败。"));
    }
}
