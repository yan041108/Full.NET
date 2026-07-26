using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantChangedCacheInvalidationHandlerTests
{
    [TestMethod]
    public async Task HandleAsync_WhenCachePropagationFails_PropagatesFailure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IFusionCache>();
        cache.SetupBackplane(new ThrowingBackplane());
        var serializer = new MessagePackIntegrationEventSerializer();
        var handler = new TenantChangedCacheInvalidationHandler(
            serializer,
            new TenantCacheInvalidator(
                cache,
                new TestHostEnvironment("Testing")));
        var payload = serializer.Serialize(new TenantChangedIntegrationEvent(
            Guid.CreateVersion7(),
            "acme.localhost"));

        await Assert.ThrowsExactlyAsync<FusionCacheBackplaneException>(
            () => handler.HandleAsync(payload, CancellationToken.None));

        var distributedServices = new ServiceCollection();
        distributedServices.AddLogging();
        distributedServices
            .AddFusionCache()
            .WithSystemTextJsonSerializer();
        await using var distributedProvider =
            distributedServices.BuildServiceProvider();
        var distributedCache =
            distributedProvider.GetRequiredService<IFusionCache>();
        distributedCache.SetupDistributedCache(new ThrowingDistributedCache());
        var distributedHandler = new TenantChangedCacheInvalidationHandler(
            serializer,
            new TenantCacheInvalidator(
                distributedCache,
                new TestHostEnvironment("Testing")));

        await Assert.ThrowsExactlyAsync<FusionCacheDistributedCacheException>(
            () => distributedHandler.HandleAsync(
                payload,
                CancellationToken.None));
    }

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
