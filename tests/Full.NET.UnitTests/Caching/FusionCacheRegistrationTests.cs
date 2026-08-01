using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.MicrosoftHybridCache;

namespace Full.NET.UnitTests.Caching;

[TestClass]
public sealed class FusionCacheRegistrationTests
{
    [TestMethod]
    public async Task AddFullNetCaching_ExposesOneFusionCacheThroughBothAbstractions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Development");

        Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == typeof(HybridCache)));

        await using var provider = services.BuildServiceProvider();
        var fusion = provider.GetRequiredService<IFusionCache>();
        var hybrid = provider.GetRequiredService<HybridCache>();
        var adapter = (FusionHybridCache)hybrid;

        Assert.AreSame(fusion, adapter.InnerFusionCache);
    }

    [TestMethod]
    public void AddFullNetCaching_RejectsMalformedRedisConnectionStringWithoutEchoingSecret()
    {
        const string malformedConnectionString =
            "localhost,abortConnect=not-a-bool,password=cache-secret-value";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisConnectionString"] = malformedConnectionString,
            })
            .Build();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            new ServiceCollection().AddFullNetCaching(configuration, "Development"));

        StringAssert.Contains(exception.Message, "Cache:RedisConnectionString");
        Assert.IsFalse(
            exception.Message.Contains(
                "cache-secret-value",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void AddFullNetCaching_Production_rejects_shared_realtime_redis()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisConnectionString"] = "127.0.0.1:6379",
                ["Realtime:RedisBackplaneConnectionString"] = "127.0.0.1:6379",
            })
            .Build();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            new ServiceCollection().AddFullNetCaching(configuration, "Production"));

        StringAssert.Contains(exception.Failures.First(), "must differ");
    }

    [TestMethod]
    public void AddFullNetCaching_Production_ignores_shared_connection_strings_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:redis"] = "127.0.0.1:6379",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(configuration, "Production");
        using var provider = services.BuildServiceProvider();

        Assert.IsNull(
            provider.GetRequiredService<IOptions<CacheOptions>>().Value.RedisConnectionString);
    }
}
