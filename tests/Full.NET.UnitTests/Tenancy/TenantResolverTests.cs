using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenantResolverTests
{
    [TestMethod]
    public async Task ResolveByDomainAsync_NormalizesAndCachesActiveTenant()
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        executor
            .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantResolutionRecord?>(ToRecord(expected)));

        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(
            executor,
            provider.GetRequiredService<HybridCache>(),
            policies,
            CreateEnvironment());

        var first = await resolver.ResolveByDomainAsync("  Acme.LocalHost  ");
        var second = await resolver.ResolveByDomainAsync("acme.localhost");

        Assert.AreEqual(expected, first);
        Assert.AreEqual(expected.Id, second?.Id);
        await executor.Received(1).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            Arg.Is<SqlStatement>(statement =>
                statement != null && statement.Name == "tenancy.find_by_domain"),
            Arg.Is<object>(parameters =>
                parameters != null && ReadDomain(parameters) == "acme.localhost"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveByDomainAsync_NegativeCachesMissingTenantForOneMinute()
    {
        var executor = Substitute.For<IQueryExecutor>();
        executor
            .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantResolutionRecord?>(null));

        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(
            executor,
            provider.GetRequiredService<HybridCache>(),
            policies,
            CreateEnvironment());

        Assert.IsNull(await resolver.ResolveByDomainAsync("missing.localhost"));
        Assert.IsNull(await resolver.ResolveByDomainAsync("MISSING.LOCALHOST"));
        Assert.AreEqual(
            TimeSpan.FromMinutes(1),
            policies.GetRequired(CacheEntryNames.TenantResolution).NegativeDuration);
        await executor.Received(1).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            Arg.Any<SqlStatement>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveByIdAsync_uses_the_explicit_global_id_query()
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(ToRecord(expected));
        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(
            executor,
            provider.GetRequiredService<HybridCache>(),
            policies,
            CreateEnvironment());

        var result = await resolver.ResolveByIdAsync(expected.Id);

        Assert.AreEqual(expected, result);
        await executor.Received(1).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            TenantSql.FindById,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetAvailableAsync_returns_the_stably_ordered_global_query()
    {
        var executor = Substitute.For<IQueryExecutor>();
        executor.QueryAsync<TenantResolutionRecord>(
                TenantSql.GetAvailable,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<TenantResolutionRecord>>(
                Array.Empty<TenantResolutionRecord>()));
        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(
            executor,
            provider.GetRequiredService<HybridCache>(),
            policies,
            CreateEnvironment());

        var result = await resolver.GetAvailableAsync();

        Assert.HasCount(0, result);
        await executor.Received(1).QueryAsync<TenantResolutionRecord>(
            TenantSql.GetAvailable,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    private static TenantSummary CreateSummary() => new(
        Guid.CreateVersion7(),
        "acme",
        "Acme",
        "acme.localhost",
        true,
        1);

    private static TenantResolutionRecord ToRecord(TenantSummary summary) => new(
        summary.Id,
        summary.Identifier,
        summary.Name,
        summary.Domain,
        summary.IsActive,
        summary.Version,
        summary.DefaultLocale);

    private static ServiceProvider CreateCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        return services.BuildServiceProvider();
    }

    private static IHostEnvironment CreateEnvironment()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        return environment;
    }

    private static string? ReadDomain(object parameters) =>
        parameters.GetType().GetProperty("Domain")?.GetValue(parameters) as string;
}
