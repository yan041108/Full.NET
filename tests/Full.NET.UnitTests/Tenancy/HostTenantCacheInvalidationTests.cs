using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Auditing;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
[DoNotParallelize]
public sealed class HostTenantCacheInvalidationTests
{
    [TestMethod]
    public async Task UpdateAsync_InvalidatesCacheDirectlyAfterCommit_WithoutOutbox()
    {
        var invalidationMeasurements = new List<InvalidationMeasurement>();
        using var listener = CreateInvalidationListener(
            invalidationMeasurements);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var tenantId = Guid.CreateVersion7();
        const string domain = "updated.localhost";
        var existing = new TenantResolutionRecord(
            tenantId,
            "updated",
            "更新前",
            domain,
            true,
            1,
            "zh-CN");
        var updated = new HostTenantRecord(
            tenantId,
            existing.Identifier,
            "更新后",
            domain,
            true,
            2,
            existing.DefaultLocale,
            null,
            null,
            null);
        queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(existing);
        queryExecutor.QuerySingleOrDefaultAsync<HostTenantRecord>(
                TenantSql.FindHostTenantById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(updated);
        commandExecutor.ExecuteAsync(
                TenantSql.UpdateHostTenantName,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        await using var provider = services.BuildServiceProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var fusionCache = provider.GetRequiredService<IFusionCache>();
        const string environmentName = "Testing";
        var tenantKey = CacheKeyBuilder.TenantResolutionById(environmentName, tenantId);
        var domainKey = CacheKeyBuilder.TenantResolutionByDomain(environmentName, domain);
        await hybridCache.SetAsync(tenantKey, "stale-tenant");
        await hybridCache.SetAsync(domainKey, "stale-domain");
        var transaction = new ObservingTransaction(async () =>
        {
            Assert.AreEqual(
                "stale-tenant",
                await hybridCache.GetOrCreateAsync(
                    tenantKey,
                    _ => ValueTask.FromResult("fresh-tenant")));
            Assert.AreEqual(
                "stale-domain",
                await hybridCache.GetOrCreateAsync(
                    domainKey,
                    _ => ValueTask.FromResult("fresh-domain")));
        });
        var service = new HostTenantManagementService(
            queryExecutor,
            commandExecutor,
            transaction,
            new HostTenantQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            Substitute.For<IClock>(),
            TenantCacheInvalidatorTestFactory.Create(
                fusionCache,
                new TestHostEnvironment(environmentName)),
            // UpdateAsync 不触碰 B0 域内审计写入器；此处只需满足构造函数依赖。
            Substitute.For<ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>>());

        var result = await service.UpdateAsync(
            tenantId,
            new UpdateHostTenantRequest("更新后", 1));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(transaction.ObservedBeforeCommit);
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
        var distributedSuccess = invalidationMeasurements.Single(item =>
            item.Tags.Any(tag =>
                tag.Key == "scope"
                && Equals(tag.Value, "distributed")));
        Assert.AreEqual("fullnet.cache.invalidation.duration", distributedSuccess.Name);
        Assert.AreEqual(
            "success",
            distributedSuccess.Tags.Single(tag => tag.Key == "outcome").Value);
        Assert.IsTrue(
            invalidationMeasurements.Any(item =>
                item.Name == "fullnet.cache.invalidation.duration"
                && item.Tags.Any(tag =>
                    tag.Key == "scope"
                    && Equals(tag.Value, "local"))
                && item.Tags.Any(tag =>
                    tag.Key == "outcome"
                    && Equals(tag.Value, "success"))),
            "提交后失效必须先记录本地 L1 成功指标。");
    }

    private static MeterListener CreateInvalidationListener(
        List<InvalidationMeasurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == CacheReliabilityTelemetry.MeterName
                    && instrument.Name == "fullnet.cache.invalidation.duration")
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
        listener.Start();
        return listener;
    }

    private sealed record InvalidationMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed class ObservingTransaction(Func<Task> observeBeforeCommit)
        : ICommandTransaction
    {
        public bool ObservedBeforeCommit { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            var result = await action(cancellationToken);
            await observeBeforeCommit();
            ObservedBeforeCommit = true;
            return result;
        }
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
