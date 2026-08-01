using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.DangerZone;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>
/// 证明 C0/S0-L2/S1 多实例边界：L1 禁用、Backplane、丢包 TTL 收敛、L2 失败、冷启动与防击穿默认策略。
/// Capacity-not-verified：本文件只验证正确性与故障边界，不宣称固定吞吐。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheConsistencyClassAssertions
{
    private const string SharedL2Entry = "demo.shared-l2";
    private const string ImportantEntry = "demo.important";

    [TestMethod]
    public async Task S0_L2_get_or_set_does_not_populate_node_memory_cache()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        await using var provider = BuildCacheProvider(redis, Guid.NewGuid().ToString("N"));
        var cache = provider.GetRequiredService<IFusionCache>();
        var registry = provider.GetRequiredService<ICachePolicyRegistry>();
        var options = registry.CreateEntryOptions(SharedL2Entry);
        var key = $"s0-{Guid.NewGuid():N}";

        var value = await cache.GetOrSetAsync<string>(
            key,
            async (_, _) => "shared-value",
            options);
        Assert.AreEqual("shared-value", value);

        var memoryOnly = options.Duplicate();
        memoryOnly.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var memoryHit = await cache.TryGetAsync<string>(key, memoryOnly);
        Assert.IsFalse(
            memoryHit.HasValue,
            "S0-L2 不得把条目回填到节点 L1，否则多实例会出现不可广播的本地漂移。");
    }

    [TestMethod]
    public async Task S1_backplane_invalidation_clears_secondary_l1()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var primaryId = $"primary-{Guid.NewGuid():N}";
        var secondaryId = $"secondary-{Guid.NewGuid():N}";
        await using var primaryProvider = BuildCacheProvider(redis, primaryId);
        await using var secondaryProvider = BuildCacheProvider(redis, secondaryId);
        var primary = primaryProvider.GetRequiredService<IFusionCache>();
        var secondary = secondaryProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotEqual(primary.InstanceId, secondary.InstanceId);

        var registry = primaryProvider.GetRequiredService<ICachePolicyRegistry>();
        var options = registry.CreateEntryOptions(ImportantEntry);
        var key = $"s1-{Guid.NewGuid():N}";

        await primary.SetAsync(key, "v1", options);
        await secondary.SetAsync(key, "v1", options);

        var removeOptions = options.Duplicate();
        removeOptions.AllowBackgroundDistributedCacheOperations = false;
        removeOptions.AllowBackgroundBackplaneOperations = false;
        removeOptions.ReThrowDistributedCacheExceptions = true;
        removeOptions.ReThrowBackplaneExceptions = true;
        await primary.RemoveAsync(key, removeOptions);

        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        MaybeValue<string> secondaryValue = default;
        while (DateTime.UtcNow < timeoutAt)
        {
            secondaryValue = await secondary.TryGetAsync<string>(key, options);
            if (!secondaryValue.HasValue)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.IsFalse(
            secondaryValue.HasValue,
            "S1 提交后直接失效必须经 Backplane 清理其他实例 L1。");
    }

    [TestMethod]
    public async Task Lost_pubsub_converges_via_short_l1_ttl()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        await using var primaryProvider = BuildCacheProvider(redis, $"p-{Guid.NewGuid():N}");
        await using var secondaryProvider = BuildCacheProvider(redis, $"s-{Guid.NewGuid():N}");
        var primary = primaryProvider.GetRequiredService<IFusionCache>();
        var secondary = secondaryProvider.GetRequiredService<IFusionCache>();
        var options = CreateShortLivedOptions(
            primaryProvider.GetRequiredService<ICachePolicyRegistry>());
        var key = $"ttl-{Guid.NewGuid():N}";

        await secondary.SetAsync(key, "stale", options);
        secondary.RemoveBackplane();
        secondary.SetupBackplane(new SilentBackplane());

        var removeOptions = options.Duplicate();
        removeOptions.AllowBackgroundDistributedCacheOperations = false;
        removeOptions.AllowBackgroundBackplaneOperations = false;
        await primary.RemoveAsync(key, removeOptions);

        Assert.IsTrue(
            (await secondary.TryGetAsync<string>(key, CreateMemoryOnlyOptions(options))).HasValue,
            "Backplane 丢失时应先观察到 secondary L1 仍可能短暂陈旧。");

        var timeoutAt = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (!(await secondary.TryGetAsync<string>(key, CreateMemoryOnlyOptions(options))).HasValue)
            {
                // L1 已过期；L2 已被 primary 删除，权威读应 miss。
                Assert.IsFalse((await secondary.TryGetAsync<string>(key, options)).HasValue);
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("丢失 Pub/Sub 后 secondary L1 未在 TTL 窗口内收敛。");
    }

    [TestMethod]
    public async Task L2_remove_failure_still_clears_local_memory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().WithSystemTextJsonSerializer();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IFusionCache>();
        cache.SetupDistributedCache(new ThrowingDistributedCache());
        const string key = "l2-fail-local";
        await cache.SetAsync(key, "stale");

        var localOnly = new FusionCacheEntryOptions();
        localOnly.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        await cache.RemoveAsync(key, localOnly);

        Assert.IsFalse((await cache.TryGetAsync<string>(key, localOnly)).HasValue);
    }

    [TestMethod]
    public async Task Missed_invalidation_without_outbox_still_converges_by_ttl()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        await using var provider = BuildCacheProvider(redis, Guid.NewGuid().ToString("N"));
        var cache = provider.GetRequiredService<IFusionCache>();
        var options = CreateShortLivedOptions(
            provider.GetRequiredService<ICachePolicyRegistry>());
        var key = $"missed-{Guid.NewGuid():N}";
        await cache.SetAsync(key, "stale", options);

        // 模拟“提交后进程终止”：业务已提交但本进程未发出失效；只依赖 TTL/权威源。
        var timeoutAt = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (!(await cache.TryGetAsync<string>(key, options)).HasValue)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("未发出失效时条目未在 TTL 后过期，说明缺少权威收敛兜底。");
    }

    private static FusionCacheEntryOptions CreateShortLivedOptions(
        ICachePolicyRegistry registry)
    {
        var options = registry.CreateEntryOptions(ImportantEntry);
        // 关闭抖动，避免短 TTL 场景被默认 Jitter 拉长到测试窗口之外。
        options.Duration = TimeSpan.FromMilliseconds(300);
        options.MemoryCacheDuration = TimeSpan.FromMilliseconds(300);
        options.JitterMaxDuration = TimeSpan.Zero;
        options.IsFailSafeEnabled = false;
        return options;
    }

    private static FusionCacheEntryOptions CreateMemoryOnlyOptions(
        FusionCacheEntryOptions source)
    {
        var options = source.Duplicate();
        options.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        return options;
    }

    [TestMethod]
    public async Task Redis_cold_start_allows_authority_factory_refill()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        await using var provider = BuildCacheProvider(redis, Guid.NewGuid().ToString("N"));
        var cache = provider.GetRequiredService<IFusionCache>();
        var key = $"cold-{Guid.NewGuid():N}";
        var options = provider.GetRequiredService<ICachePolicyRegistry>()
            .CreateEntryOptions(ImportantEntry);
        await cache.SetAsync(key, "warm", options);

        // 清空共享 L2 中该条目，模拟 Redis 冷启动后只剩权威源可回填。
        await using var mux = await ConnectionMultiplexer.ConnectAsync(redis);
        var server = mux.GetServers().First(item => item.IsConnected);
        foreach (var redisKey in server.Keys(pattern: $"*{key}*"))
        {
            await mux.GetDatabase().KeyDeleteAsync(redisKey);
        }

        var localOnly = options.Duplicate();
        localOnly.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        await cache.RemoveAsync(key, localOnly);

        var factoryCalls = 0;
        var value = await cache.GetOrSetAsync<string>(
            key,
            async (_, _) =>
            {
                Interlocked.Increment(ref factoryCalls);
                return "refilled";
            },
            options);
        Assert.AreEqual("refilled", value);
        Assert.AreEqual(1, factoryCalls);
    }

    [TestMethod]
    public async Task High_cardinality_missing_keys_do_not_throw_or_require_hot_key_lock()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        await using var provider = BuildCacheProvider(redis, Guid.NewGuid().ToString("N"));
        var cache = provider.GetRequiredService<IFusionCache>();
        var options = provider.GetRequiredService<ICachePolicyRegistry>()
            .CreateEntryOptions(ImportantEntry);
        var factoryCalls = 0;

        for (var i = 0; i < 64; i++)
        {
            var key = $"miss-{Guid.NewGuid():N}";
            var value = await cache.GetOrSetAsync<string>(
                key,
                async (_, _) =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return "miss";
                },
                options);
            Assert.AreEqual("miss", value);
        }

        Assert.AreEqual(64, factoryCalls);
        AssertNoHotKeyLockRegistration(provider);
    }

    [TestMethod]
    public async Task Concurrent_same_key_requests_coalesce_without_distributed_hot_key_lock()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        await using var provider = BuildCacheProvider(redis, Guid.NewGuid().ToString("N"));
        var cache = provider.GetRequiredService<IFusionCache>();
        var options = provider.GetRequiredService<ICachePolicyRegistry>()
            .CreateEntryOptions(ImportantEntry);
        var key = $"merge-{Guid.NewGuid():N}";
        var factoryCalls = 0;

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrSetAsync<string>(
                key,
                async (_, _) =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(75);
                    return "merged";
                },
                options).AsTask())
            .ToArray();
        var values = await Task.WhenAll(tasks);

        Assert.IsTrue(values.All(value => value == "merged"));
        Assert.IsLessThanOrEqualTo(
            2,
            factoryCalls,
            "默认应依赖 FusionCache 同 Key 合并，而不是自定义全局锁；factory 调用应被合并。");
        AssertNoHotKeyLockRegistration(provider);
    }

    private static void AssertNoHotKeyLockRegistration(IServiceProvider provider)
    {
        // 首版默认只依赖 FusionCache 同 Key 合并；显式热点锁需有跨实例放大证据后再开启。
        Assert.IsNull(
            provider.GetService(typeof(SemaphoreSlim)),
            "未配置热点锁时不得注册全局 SemaphoreSlim 作为回填锁。");
    }

    private static ServiceProvider BuildCacheProvider(
        string redisConnectionString,
        string instanceId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CacheOptions.SectionName}:RedisConnectionString"] = redisConnectionString,
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:OwnerModule"] = "demo",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:ConsistencyClass"] = "S0-L2",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:L2Duration"] = "00:05:00",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:MaxSerializedBytes"] = "65536",
                [$"{CacheOptions.SectionName}:Entries:{ImportantEntry}:OwnerModule"] = "demo",
                [$"{CacheOptions.SectionName}:Entries:{ImportantEntry}:ConsistencyClass"] = "S1",
                [$"{CacheOptions.SectionName}:Entries:{ImportantEntry}:L1Duration"] = "00:05:00",
                [$"{CacheOptions.SectionName}:Entries:{ImportantEntry}:L2Duration"] = "00:05:00",
                [$"{CacheOptions.SectionName}:Entries:{ImportantEntry}:MaxSerializedBytes"] = "65536",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(configuration, "Testing");
        services.PostConfigure<FusionCacheOptions>(options =>
            FusionCacheDangerZoneUtils.SetInstanceId(options, instanceId));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class SilentBackplane : IFusionCacheBackplane
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
            CancellationToken token = default)
        {
        }

        public ValueTask PublishAsync(
            BackplaneMessage message,
            FusionCacheEntryOptions options,
            CancellationToken token = default) =>
            ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) =>
            throw new InvalidOperationException("模拟 L2 读取失败。");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromException<byte[]?>(new InvalidOperationException("模拟 L2 读取失败。"));

        public void Refresh(string key) =>
            throw new InvalidOperationException("模拟 L2 刷新失败。");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("模拟 L2 刷新失败。"));

        public void Remove(string key) =>
            throw new InvalidOperationException("模拟 L2 删除失败。");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("模拟 L2 删除失败。"));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("模拟 L2 写入失败。");

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("模拟 L2 写入失败。"));
    }
}
