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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Full.NET.UnitTests.Caching;

/// <summary>单元测试（缓存）：Fusion Cache Registration。</summary>
[TestClass]
public sealed class FusionCacheRegistrationTests
{
    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("67108865")]
    public void Registration_rejects_unbounded_deserialization_limits(string maximum)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Cache:MaxDeserializationBytes"] = maximum }).Build();
        Assert.ThrowsExactly<OptionsValidationException>(() => new ServiceCollection()
            .AddFullNetCaching(configuration, "Testing"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Registered_serializer_rejects_oversized_L2_input_before_JSON_parse(bool preferSync)
    {
        await using var provider = CreateInputLimitProvider(64, preferSync);
        var cache = provider.GetRequiredService<IFusionCache>();
        var key = $"input-limit-{Guid.NewGuid():N}";
        var options = new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1), ReThrowSerializationExceptions = true,
            AllowBackgroundDistributedCacheOperations = false,
        };
        await cache.SetAsync(key, new string('中', 1000), options);
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        await cache.RemoveAsync(key, local);

        var error = await Assert.ThrowsExactlyAsync<FusionCacheSerializationException>(async () =>
            { await cache.TryGetAsync<string>(key, options); });
        Assert.AreEqual("CacheDeserializationLimitException", error.InnerException?.GetType().Name);
        Assert.DoesNotContain("中", error.Message);
    }

    [TestMethod]
    public async Task Registered_serializer_keeps_small_L2_round_trips_compatible()
    {
        await using var provider = CreateInputLimitProvider(16_384, preferSync: false);
        var cache = provider.GetRequiredService<IFusionCache>();
        var key = $"input-compatible-{Guid.NewGuid():N}";
        await cache.SetAsync(key, "known-value");
        var local = new FusionCacheEntryOptions();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        await cache.RemoveAsync(key, local);
        var value = await cache.TryGetAsync<string>(key);
        Assert.IsTrue(value.HasValue);
        Assert.AreEqual("known-value", value.Value);
    }

    private static ServiceProvider CreateInputLimitProvider(int maximum, bool preferSync)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Cache:MaxDeserializationBytes"] = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .Build(), "Testing");
        services.PostConfigureAll<FusionCacheOptions>(options => options.PreferSyncSerialization = preferSync);
        var provider = services.BuildServiceProvider();
        // 保留生产注册的序列化器，只接入内存 L2 替身验证其真实 SDK 读取路径。
        provider.GetRequiredService<IFusionCache>().SetupDistributedCache(provider.GetRequiredService<IDistributedCache>());
        return provider;
    }

    [TestMethod]
    public async Task Health_probe_deadline_does_not_depend_on_the_L2_honoring_cancellation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = "127.0.0.1:1,connectRetry=0",
            ["Cache:RedisOperationTimeout"] = "00:00:00.050",
        }).Build(), "Testing");
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var distributed = Substitute.For<IDistributedCache>();
        distributed.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(completion.Task);
        services.AddSingleton(distributed);
        await using var provider = services.BuildServiceProvider();
        var check = provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        try
        {
            var winner = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.AreSame(check, winner, "探针必须在自己的预算内结束，不等待不响应取消的底层命令。");
            Assert.AreEqual(HealthStatus.Degraded, (await check).Entries["distributed-cache"].Status);
        }
        finally { completion.TrySetResult(null); await check; }
    }

    [TestMethod]
    public async Task Health_probe_reuses_the_registered_L2_instead_of_creating_an_unowned_connection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = "127.0.0.1:1,connectRetry=0",
        }).Build(), "Testing");
        var distributed = Substitute.For<IDistributedCache>();
        services.AddSingleton(distributed);
        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        // 探针不主动初始化 Backplane；只有 L2 GET 的证据时不能宣称广播健康。
        Assert.AreEqual(HealthStatus.Degraded, result.Entries["distributed-cache"].Status);
        await distributed.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Registered_S2_hybrid_path_preserves_fail_safe_and_its_explicit_retention_limit()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Jitter"] = "00:00:00",
            ["Cache:Entries:demo.display:OwnerModule"] = "demo",
            ["Cache:Entries:demo.display:ConsistencyClass"] = "S2",
            ["Cache:Entries:demo.display:L1Duration"] = "00:00:00.0000001",
            ["Cache:Entries:demo.display:L2Duration"] = "00:00:00.0000001",
            ["Cache:Entries:demo.display:FailSafeEnabled"] = "true",
            ["Cache:Entries:demo.display:FailSafeMaxDuration"] = "00:01:00",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(configuration, "Testing");
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ICachePolicyRegistry>();
        var hybrid = provider.GetRequiredService<HybridCache>();
        var key = CacheKeyBuilder.ForGlobal("Testing", "demo", "display", "one", "v1");
        var options = registry.CreateHybridEntryOptions("demo.display");
        await hybrid.SetAsync(key, "retained", options);
        var value = await hybrid.GetOrCreateAsync<string>(key,
            _ => ValueTask.FromException<string>(new InvalidOperationException("authority unavailable")), options);
        Assert.AreEqual("retained", value);
        Assert.AreEqual(TimeSpan.FromMinutes(1), registry.CreateEntryOptions("demo.display").FailSafeMaxDuration);
    }

    [TestMethod]
    public async Task Redis_budgets_and_application_environment_namespace_reach_sdk_options()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:ApplicationName"] = "orders",
            ["Cache:RedisOperationTimeout"] = "00:00:00.250",
            ["Cache:AutoRecoveryMaxItems"] = "27",
            ["Cache:AutoRecoveryMaxRetryCount"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(configuration, "Testing");
        await using var provider = services.BuildServiceProvider();
        var fusion = provider.GetRequiredService<IFusionCache>();
        // WithOptions 配置的是 Builder 创建的实例选项，不是命名 IOptionsMonitor 的默认值。
        var options = (FusionCacheOptions)typeof(FusionCache)
            .GetField("_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fusion)!;
        StringAssert.Contains(options.BackplaneChannelPrefix!, "testing:orders");
        Assert.AreEqual(27, options.AutoRecoveryMaxItems);
        Assert.AreEqual(2, options.AutoRecoveryMaxRetryCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), fusion.DefaultEntryOptions.DistributedCacheHardTimeout);
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), provider.GetRequiredService<ICachePolicyRegistry>()
            .CreateEntryOptions(CacheEntryNames.GridPreference).DistributedCacheHardTimeout);
    }

    [TestMethod]
    public async Task Migrator_in_production_has_no_redis_dependency_and_uses_application_namespace()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetMigratorCaching(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Cache:RedisConnectionString"] = "unused:6379" }).Build(), "Production");
        await using var provider = services.BuildServiceProvider();
        Assert.IsNull(provider.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>());
        Assert.IsNull(provider.GetRequiredService<IOptions<CacheOptions>>().Value.RedisConnectionString);
        var fusion = provider.GetRequiredService<IFusionCache>();
        var options = (FusionCacheOptions)typeof(FusionCache)
            .GetField("_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fusion)!;
        Assert.AreEqual("fullnet:production:fullnet:v2", options.BackplaneChannelPrefix);
        Assert.IsNotNull(provider.GetRequiredService<HybridCache>());
    }

    [TestMethod]
    [DataRow("Cache:InvalidationTimeout", "00:00:00")]
    [DataRow("Cache:InvalidationTimeout", "00:10:00")]
    [DataRow("Cache:ApplicationName", "invalid:namespace")]
    [DataRow("Cache:RedisOperationTimeout", "00:00:00")]
    [DataRow("Cache:AutoRecoveryMaxItems", "0")]
    [DataRow("Cache:AutoRecoveryMaxRetryCount", "0")]
    public void Cache_resource_budgets_are_validated(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [key] = value }).Build();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            new ServiceCollection().AddFullNetCaching(configuration, "Testing"));
    }

    [TestMethod]
    [DataRow("Production")]
    [DataRow("Staging")]
    public void Production_like_environment_requires_dedicated_cache_redis(string environment)
    {
        Assert.ThrowsExactly<OptionsValidationException>(() => new ServiceCollection()
            .AddFullNetCaching(new ConfigurationBuilder().Build(), environment));
    }

    [TestMethod]
    [DataRow("redis.internal:6379,defaultDatabase=1,password=sample-a,abortConnect=false", "REDIS.INTERNAL:6379,password=sample-b,abortConnect=true,defaultDatabase=2", "Production", false)]
    [DataRow("redis.internal", "redis.internal:6379", "Production", false)]
    [DataRow("redis.internal:6379", "redis.internal", "Staging", false)]
    [DataRow("redis.internal,ssl=true", "redis.internal:6380,ssl=true", "Production", false)]
    [DataRow("redis.internal:6380,ssl=true", "redis.internal,ssl=true", "Staging", false)]
    [DataRow("127.0.0.1", "127.0.0.1:6379", "Production", false)]
    [DataRow("[::1]", "[::1]:6379", "Staging", false)]
    [DataRow("a.internal:6379,b.internal:6380", "B.INTERNAL:6380,c.internal:6379", "Production", false)]
    [DataRow("a.internal,b.internal:6380", "c.internal:6379,A.INTERNAL:6379", "Staging", false)]
    [DataRow("sentinel.internal,serviceName=cache", "sentinel.internal:26379,serviceName=realtime", "Production", false)]
    [DataRow("sentinel.internal:26379,serviceName=cache,ssl=true", "sentinel.internal,serviceName=realtime,ssl=true", "Staging", false)]
    [DataRow("redis.internal", "redis.internal:6379", "Production", true)]
    [DataRow("redis.internal,ssl=true", "redis.internal:6380,ssl=true", "Staging", true)]
    public void Production_rejects_same_redis_endpoint_with_different_database_and_options(
        string cacheConnection, string realtimeConnection, string environment, bool allowShared)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = cacheConnection,
            ["Realtime:RedisBackplaneConnectionString"] = realtimeConnection,
            ["Realtime:AllowSharedRedisInDevelopment"] = allowShared.ToString(),
        }).Build();
        var error = Assert.ThrowsExactly<OptionsValidationException>(() => new ServiceCollection()
            .AddFullNetCaching(configuration, environment));
        StringAssert.Contains(error.Message, "must differ");
        Assert.DoesNotContain("sample-", error.Message, "准入失败不得把凭据带入错误消息。");
    }

    [TestMethod]
    [DataRow("a.internal:6379", "b.internal:6379", "Production", false)]
    [DataRow("redis.internal:6379", "redis.internal:6380", "Production", false)]
    [DataRow("redis.internal", "redis.internal,ssl=true", "Production", false)]
    [DataRow("redis.internal", "redis.internal:6379", "Testing", true)]
    public void Distinct_redis_endpoints_and_explicit_testing_sharing_are_admitted(
        string cacheConnection, string realtimeConnection, string environment, bool allowShared)
    {
        var services = new ServiceCollection();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = cacheConnection,
            ["Realtime:RedisBackplaneConnectionString"] = realtimeConnection,
            ["Realtime:AllowSharedRedisInDevelopment"] = allowShared.ToString(),
        }).Build(), environment);
        // 只检验注册准入，不构造缓存客户端或以连接结果替代配置语义。
        Assert.IsTrue(services.Any(descriptor => descriptor.ServiceType == typeof(ICachePolicyRegistry)));
    }

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
        Assert.IsFalse(implementationTypes.Any(type => type?.Assembly == typeof(TenancyModule).Assembly));
    }

    [TestMethod]
    public void Aot_serializer_resolves_module_owned_cache_payloads()
    {
        var serializer = new FullNetFusionCacheJsonSerializer(
        [
            new SettingsCacheJsonTypeInfoContributor(),
        ]);
        var grid = new GridPreferenceResponse(
            "identity.users",
            1,
            [new GridColumnPreference("displayName", 0, 240, true, null)],
            3);
        var gridRoundTrip = serializer.Deserialize<GridPreferenceResponse>(
            serializer.Serialize(grid));

        Assert.IsNotNull(gridRoundTrip);
        Assert.AreEqual(grid.GridKey, gridRoundTrip.GridKey);
        Assert.AreEqual(grid.SchemaVersion, gridRoundTrip.SchemaVersion);
        Assert.AreEqual(grid.Version, gridRoundTrip.Version);
        CollectionAssert.AreEqual(grid.Columns.ToArray(), gridRoundTrip.Columns.ToArray());
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
    public void Authority_and_snapshot_policies_cannot_be_reconfigured_as_cached_reads()
    {
        foreach (var entry in new[] { CacheEntryNames.TenantResolution, CacheEntryNames.DiagnosticPolicy })
            Assert.ThrowsExactly<OptionsValidationException>(() => CachePolicyRegistry.Create(new CacheOptions
            {
                Entries = { [entry] = new CacheEntryDefinitionOptions
                {
                    OwnerModule = entry.Split('.')[0], ConsistencyClass = "S2",
                    L1Duration = TimeSpan.FromSeconds(5), L2Duration = TimeSpan.FromSeconds(30),
                } },
            }));
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

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            new ServiceCollection().AddFullNetCaching(configuration, "Production"));
    }

    private sealed record UnregisteredCachePayload(string Value);
}
