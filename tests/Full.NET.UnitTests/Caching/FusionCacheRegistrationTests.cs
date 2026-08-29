using Full.NET.Caching.Fusion;
using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings;
using Full.NET.Modules.Settings.Serialization;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Serialization;
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
    public void Cache_payload_owners_register_their_aot_json_contributors()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        new SettingsModule().AddServices(services, configuration);
        new TenancyModule().AddServices(services, configuration);

        var implementationTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICacheJsonTypeInfoContributor))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        CollectionAssert.Contains(
            implementationTypes,
            typeof(SettingsCacheJsonTypeInfoContributor));
        CollectionAssert.Contains(
            implementationTypes,
            typeof(TenancyCacheJsonTypeInfoContributor));
    }

    [TestMethod]
    public void Aot_serializer_resolves_module_owned_cache_payloads()
    {
        var serializer = new FullNetFusionCacheJsonSerializer(
        [
            new SettingsCacheJsonTypeInfoContributor(),
            new TenancyCacheJsonTypeInfoContributor(),
        ]);
        var grid = new GridPreferenceResponse(
            "identity.users",
            1,
            [new GridColumnPreference("displayName", 0, 240, true, null)],
            3);
        var tenant = new TenantResolutionCacheEntry(new TenantCachePayload(
            Guid.Parse("0199382f-f88d-7000-8000-000000000001"),
            "northwind",
            "Northwind",
            "northwind.example.test",
            true,
            7,
            "zh-CN",
            null,
            null,
            null));

        var gridRoundTrip = serializer.Deserialize<GridPreferenceResponse>(
            serializer.Serialize(grid));
        var tenantRoundTrip = serializer.Deserialize<TenantResolutionCacheEntry>(
            serializer.Serialize(tenant));

        Assert.IsNotNull(gridRoundTrip);
        Assert.AreEqual(grid.GridKey, gridRoundTrip.GridKey);
        Assert.AreEqual(grid.SchemaVersion, gridRoundTrip.SchemaVersion);
        Assert.AreEqual(grid.Version, gridRoundTrip.Version);
        CollectionAssert.AreEqual(grid.Columns.ToArray(), gridRoundTrip.Columns.ToArray());
        Assert.AreEqual(tenant, tenantRoundTrip);
    }

    [TestMethod]
    public void Aot_serializer_rejects_unregistered_cache_payload()
    {
        var serializer = new FullNetFusionCacheJsonSerializer([]);

        var exception = Assert.ThrowsExactly<NotSupportedException>(() =>
            serializer.Serialize(new UnregisteredCachePayload("value")));

        StringAssert.Contains(exception.Message, typeof(UnregisteredCachePayload).FullName!);
    }

    [TestMethod]
    public void Tenancy_owned_cache_payload_reads_existing_l2_json_contract()
    {
        var serializer = new FullNetFusionCacheJsonSerializer(
        [
            new TenancyCacheJsonTypeInfoContributor(),
        ]);
        var existingPayload = System.Text.Encoding.UTF8.GetBytes(
            """
            {"tenant":{"id":"0199382f-f88d-7000-8000-000000000001","identifier":"northwind","name":"Northwind","domain":"northwind.example.test","isActive":true,"version":7,"defaultLocale":"zh-CN","tenantPackageId":null,"tenantPackageCode":null,"tenantPackageName":null}}
            """);

        var entry = serializer.Deserialize<TenantResolutionCacheEntry>(existingPayload);

        Assert.IsNotNull(entry?.Tenant);
        Assert.AreEqual("northwind", entry.Tenant.Identifier);
        Assert.AreEqual(7, entry.Tenant.Version);
        Assert.AreEqual("zh-CN", entry.Tenant.DefaultLocale);
    }

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

    private sealed record UnregisteredCachePayload(string Value);
}
