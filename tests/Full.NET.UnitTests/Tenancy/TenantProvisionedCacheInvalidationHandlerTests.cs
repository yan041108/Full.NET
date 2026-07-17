using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantProvisionedCacheInvalidationHandlerTests
{
    [TestMethod]
    public async Task AddFullNetTenancyWorkerServices_RegistersOnlyWorkerDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        services.AddSingleton<IIntegrationEventSerializer,
            MessagePackIntegrationEventSerializer>();

        services.AddFullNetTenancyWorkerServices();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        await using var scope = provider.CreateAsyncScope();
        var handlers = scope.ServiceProvider
            .GetServices<IIntegrationEventHandler>()
            .ToArray();
        Assert.HasCount(1, handlers);
        Assert.IsInstanceOfType<TenantProvisionedCacheInvalidationHandler>(
            handlers[0]);
        Assert.AreSame(
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(),
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>());
    }

    [TestMethod]
    public async Task HandleAsync_InvalidatesTenantAndDomainTags()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var serializer = new MessagePackIntegrationEventSerializer();
        var tenantId = Guid.CreateVersion7();
        const string domain = "acme.localhost";
        const string tenantKey = "test:tenant";
        const string domainKey = "test:domain";
        await cache.SetAsync(
            tenantKey,
            "stale-tenant",
            tags: [CacheKeyBuilder.TenantTag(tenantId)]);
        await cache.SetAsync(
            domainKey,
            "stale-domain",
            tags: [CacheKeyBuilder.DomainTag(domain)]);
        var handler = new TenantProvisionedCacheInvalidationHandler(
            serializer,
            cache);
        var payload = serializer.Serialize(new TenantProvisionedIntegrationEvent(
            tenantId,
            "acme",
            domain));

        await handler.HandleAsync(payload, CancellationToken.None);

        var tenantValue = await cache.GetOrCreateAsync(
            tenantKey,
            _ => ValueTask.FromResult("fresh-tenant"));
        var domainValue = await cache.GetOrCreateAsync(
            domainKey,
            _ => ValueTask.FromResult("fresh-domain"));
        Assert.AreEqual("fresh-tenant", tenantValue);
        Assert.AreEqual("fresh-domain", domainValue);
    }
}
