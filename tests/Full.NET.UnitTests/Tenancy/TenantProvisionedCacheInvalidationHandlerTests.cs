using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ProvisionTenant;
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

    [TestMethod]
    public async Task ProvisionAsync_DoesNotExposeRequestCancellationAfterCommit()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        await using var provider = services.BuildServiceProvider();
        var fusionCache = provider.GetRequiredService<IFusionCache>();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        using var requestCancellation = new CancellationTokenSource();
        var tenant = new TenantSummary(
            Guid.CreateVersion7(),
            "acme",
            "Acme",
            "acme.localhost",
            true,
            1);
        const string environmentName = "Testing";
        var tenantKey = CacheKeyBuilder.TenantResolutionById(
            environmentName,
            tenant.Id);
        var domainKey = CacheKeyBuilder.TenantResolutionByDomain(
            environmentName,
            tenant.Domain);
        await hybridCache.SetAsync(tenantKey, "stale-tenant");
        await hybridCache.SetAsync(domainKey, "stale-domain");
        var service = new TenantProvisioningService(
            new CancellingCommandDispatcher(requestCancellation, tenant),
            fusionCache,
            new TestHostEnvironment(environmentName));

        var result = await service.ProvisionAsync(
            new ProvisionTenantRequest(
                tenant.Identifier,
                tenant.Name,
                tenant.Domain),
            requestCancellation.Token);

        Assert.IsTrue(requestCancellation.IsCancellationRequested);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(tenant.Id, result.Value!.Id);
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
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Full.NET.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class CancellingCommandDispatcher(
        CancellationTokenSource requestCancellation,
        TenantSummary tenant) : ICommandDispatcher
    {
        public Task<Result<TResult>> SendAsync<TCommand, TResult>(
            TCommand command,
            CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>
        {
            Assert.IsFalse(cancellationToken.IsCancellationRequested);
            requestCancellation.Cancel();
            return Task.FromResult(
                (Result<TResult>)(object)Result<TenantSummary>.Success(tenant));
        }
    }
}
