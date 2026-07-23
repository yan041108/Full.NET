using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantProvisionedCacheInvalidationHandlerTests
{
    [TestMethod]
    public async Task AddBackgroundServices_RegistersOnlyWorkerDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment("Testing"));
        services.AddSingleton<IIntegrationEventSerializer,
            MessagePackIntegrationEventSerializer>();

        new TenancyModule().AddBackgroundServices(
            services,
            new ConfigurationBuilder().Build());

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
        Assert.AreEqual(
            "fullnet.tenancy.tenant.provisioned",
            handlers[0].EventType);
        CollectionAssert.Contains(
            handlers[0].LegacyEventTypes.ToArray(),
            "fullnet.tenancy.tenant-provisioned");
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
        var fusionCache = provider.GetRequiredService<IFusionCache>();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var serializer = new MessagePackIntegrationEventSerializer();
        var tenantId = Guid.CreateVersion7();
        const string domain = "acme.localhost";
        const string environmentName = "Testing";
        var tenantKey = CacheKeyBuilder.TenantResolutionById(
            environmentName,
            tenantId);
        var domainKey = CacheKeyBuilder.TenantResolutionByDomain(
            environmentName,
            domain);
        await hybridCache.SetAsync(
            tenantKey,
            "stale-tenant",
            tags: [CacheKeyBuilder.TenantTag(tenantId)]);
        await hybridCache.SetAsync(
            domainKey,
            "stale-domain",
            tags: [CacheKeyBuilder.DomainTag(domain)]);
        var handler = new TenantProvisionedCacheInvalidationHandler(
            serializer,
            fusionCache,
            new TestHostEnvironment(environmentName));
        var payload = serializer.Serialize(new TenantProvisionedIntegrationEvent(
            tenantId,
            "acme",
            domain));

        await handler.HandleAsync(payload, CancellationToken.None);

        var tenantValue = await hybridCache.GetOrCreateAsync(
            tenantKey,
            _ => ValueTask.FromResult("fresh-tenant"));
        var domainValue = await hybridCache.GetOrCreateAsync(
            domainKey,
            _ => ValueTask.FromResult("fresh-domain"));
        Assert.AreEqual("fresh-tenant", tenantValue);
        Assert.AreEqual("fresh-domain", domainValue);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Full.NET.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
