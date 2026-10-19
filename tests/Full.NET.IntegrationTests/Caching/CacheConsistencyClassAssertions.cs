using System.Diagnostics;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
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
        var memoryOnly = CreateMemoryOnlyOptions(options);
        var legacyKey = $"{key}-legacy";
        await cache.SetAsync(legacyKey, "legacy-local", memoryOnly);
        Assert.AreEqual("legacy-local", (await cache.TryGetAsync<string>(legacyKey, memoryOnly)).Value,
            "先证明探针可以看到真实 L1，不能用 S0 禁读选项证明 L1 不存在。");
        var legacyRead = await cache.GetOrSetAsync<string>(legacyKey, async (_, _) => "shared-current", options);
        Assert.AreEqual("shared-current", legacyRead, "S0 不得复用策略切换前的旧 L1。");
        var legacyAfterRead = await cache.TryGetAsync<string>(legacyKey, memoryOnly);
        if (legacyAfterRead.HasValue) Assert.AreEqual("legacy-local", legacyAfterRead.Value,
            "S0 回源结果不得回填 L1；清理掉旧 L1 也允许。");

        var value = await cache.GetOrSetAsync<string>(
            key,
            async (_, _) => "shared-value",
            options);
        Assert.AreEqual("shared-value", value);

        var memoryHit = await cache.TryGetAsync<string>(key, memoryOnly);
        Assert.IsFalse(
            memoryHit.HasValue,
            "S0-L2 不得把条目回填到节点 L1，否则多实例会出现不可广播的本地漂移。");
        await AssertSharedHybridLayerAsync(redis);
        await AssertSharedHybridExpirationAsync(redis);
    }

    /// <summary>用两个独立注册、真实 Redis 和无 Backplane 的 Hybrid 路径验证 S0 正常与负缓存。</summary>
    private static async Task AssertSharedHybridLayerAsync(string redisConnectionString)
    {
        await using var primaryProvider = BuildCacheProvider(redisConnectionString, $"s0-p-{Guid.NewGuid():N}");
        await using var secondaryProvider = BuildCacheProvider(redisConnectionString, $"s0-s-{Guid.NewGuid():N}");
        var primaryFusion = primaryProvider.GetRequiredService<IFusionCache>();
        var secondaryFusion = secondaryProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotSame(primaryFusion, secondaryFusion);
        Assert.AreNotEqual(primaryFusion.InstanceId, secondaryFusion.InstanceId);
        // S0 不依赖广播正确性；移除本用例两节点的 Backplane，不能用通知清理掩盖 L1 命中。
        primaryFusion.RemoveBackplane();
        secondaryFusion.RemoveBackplane();
        var primary = primaryProvider.GetRequiredService<HybridCache>();
        var secondary = secondaryProvider.GetRequiredService<HybridCache>();
        var registry = primaryProvider.GetRequiredService<ICachePolicyRegistry>();
        var memoryProbe = CreateMemoryOnlyOptions(registry.CreateEntryOptions(SharedL2Entry));
        foreach (var lifetime in new[] { CacheEntryLifetime.Normal, CacheEntryLifetime.Negative })
        {
            var options = registry.CreateHybridEntryOptions(SharedL2Entry, lifetime);
            var key = $"s0-hybrid-{lifetime}-{Guid.NewGuid():N}";
            var factoryCalls = 0;
            await secondaryFusion.SetAsync(key, "legacy-local", memoryProbe);
            Assert.AreEqual("legacy-local", (await secondaryFusion.TryGetAsync<string>(key, memoryProbe)).Value);
            await primary.SetAsync(key, "shared-v1", options);
            Assert.AreEqual("shared-v1", await secondary.GetOrCreateAsync(key, _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult("unexpected-factory");
            }, options));
            Assert.AreEqual(0, factoryCalls, "第二节点必须命中 Redis，而不是回源或旧 L1。");
            Assert.IsFalse((await primaryFusion.TryGetAsync<string>(key, memoryProbe)).HasValue);
            var legacy = await secondaryFusion.TryGetAsync<string>(key, memoryProbe);
            if (legacy.HasValue) Assert.AreEqual("legacy-local", legacy.Value, "共享读取不能回填 L1。");

            await primary.SetAsync(key, "shared-v2", options);
            Assert.AreEqual("shared-v2", await secondary.GetOrCreateAsync(key, _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult("unexpected-factory");
            }, options));
            Assert.AreEqual(0, factoryCalls);

            var factoryKey = $"{key}-factory";
            Assert.AreEqual("factory-shared", await primary.GetOrCreateAsync(factoryKey, _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult("factory-shared");
            }, options));
            Assert.AreEqual("factory-shared", await secondary.GetOrCreateAsync(factoryKey, _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult("unexpected-factory");
            }, options));
            Assert.AreEqual(1, factoryCalls);
            Assert.IsFalse((await primaryFusion.TryGetAsync<string>(factoryKey, memoryProbe)).HasValue);
            Assert.IsFalse((await secondaryFusion.TryGetAsync<string>(factoryKey, memoryProbe)).HasValue);
        }
    }

    /// <summary>同时观察 Redis 物理寿命与真实 Hybrid 回源，避免把本地过期或未缓存的 null 当成共享层过期。</summary>
    private static async Task AssertSharedHybridExpirationAsync(string redisConnectionString)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        // 实验使用九秒正常寿命、三秒负缓存、零抖动；这些窗口不是生产时效承诺。
        await using var primaryProvider = BuildCacheProvider(redisConnectionString,
            $"expiry-primary-{Guid.NewGuid():N}", shortSharedLifetime: true);
        await using var secondaryProvider = BuildCacheProvider(redisConnectionString,
            $"expiry-secondary-{Guid.NewGuid():N}", shortSharedLifetime: true);
        var primaryFusion = primaryProvider.GetRequiredService<IFusionCache>();
        var secondaryFusion = secondaryProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotSame(primaryFusion, secondaryFusion);
        Assert.AreNotEqual(primaryFusion.InstanceId, secondaryFusion.InstanceId);
        primaryFusion.RemoveBackplane();
        secondaryFusion.RemoveBackplane();
        var primary = primaryProvider.GetRequiredService<HybridCache>();
        var secondary = secondaryProvider.GetRequiredService<HybridCache>();
        var registry = primaryProvider.GetRequiredService<ICachePolicyRegistry>();
        var memoryProbe = CreateMemoryOnlyOptions(registry.CreateEntryOptions(SharedL2Entry));
        await using var mux = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
        var database = mux.GetDatabase();
        var server = mux.GetServers().Single(item => item.IsConnected);
        var cases = new List<SharedExpirationCase>();
        foreach (var lifetime in new[] { CacheEntryLifetime.Negative, CacheEntryLifetime.Normal })
        foreach (var useFactory in new[] { false, true })
        {
            var options = registry.CreateHybridEntryOptions(SharedL2Entry, lifetime);
            var entry = new SharedExpirationCase
            {
                Key = $"s0-expiry-{Guid.NewGuid():N}",
                Lifetime = lifetime,
                Options = options,
                OldValue = lifetime == CacheEntryLifetime.Negative ? null : "normal-old",
                ExpectedDuration = TimeSpan.FromSeconds(lifetime == CacheEntryLifetime.Negative ? 3 : 9),
            };
            if (useFactory)
            {
                var value = await primary.GetOrCreateAsync<string?>(entry.Key, _ =>
                {
                    entry.PrimeFactoryCalls++;
                    return ValueTask.FromResult(entry.OldValue);
                }, options, cancellationToken: token);
                Assert.AreEqual(entry.OldValue, value);
                Assert.AreEqual(1, entry.PrimeFactoryCalls);
            }
            else
            {
                await primary.SetAsync<string?>(entry.Key, entry.OldValue, options, cancellationToken: token);
            }

            var physicalKeys = new List<RedisKey>();
            await foreach (var physicalKey in server.KeysAsync(database.Database, $"*{entry.Key}*", pageSize: 100)
                .WithCancellation(token))
                physicalKeys.Add(physicalKey);
            Assert.AreEqual(1, physicalKeys.Count, "必须观察此唯一业务键的真实 Redis 记录，不能用工厂结果代替存储证明。");
            entry.RedisKey = physicalKeys[0];
            var remaining = await database.KeyTimeToLiveAsync(entry.RedisKey);
            var elapsed = entry.Clock.Elapsed;
            Assert.IsTrue(elapsed < TimeSpan.FromSeconds(1), "初始 Redis 寿命观察调度过晚，不能据此判断映射正确。");
            Assert.IsNotNull(remaining, "共享键必须存在且具有 Redis 物理过期时间。");
            // 官方 RedisCache 以整秒截断绝对过期差值；下界额外包含不足一秒的物理存储精度。
            Assert.IsTrue(remaining.Value >= entry.ExpectedDuration - elapsed - TimeSpan.FromMilliseconds(1250)
                && remaining.Value <= entry.ExpectedDuration + TimeSpan.FromMilliseconds(250),
                $"{lifetime} Redis TTL 应约为 {entry.ExpectedDuration.TotalSeconds}s，实际 {remaining.Value.TotalSeconds:F3}s。");
            Assert.AreEqual(entry.OldValue, await secondary.GetOrCreateAsync<string?>(entry.Key, _ =>
            {
                entry.ReadFactoryCalls++;
                return ValueTask.FromResult<string?>("unexpected-before-expiry");
            }, options, cancellationToken: token));
            Assert.AreEqual(0, entry.ReadFactoryCalls, "包括 null 在内，第二实例必须命中 Redis 而非再次回源。");
            Assert.IsFalse((await primaryFusion.TryGetAsync<string?>(entry.Key, memoryProbe, token)).HasValue);
            Assert.IsFalse((await secondaryFusion.TryGetAsync<string?>(entry.Key, memoryProbe, token)).HasValue);
            cases.Add(entry);
        }

        foreach (var entry in cases)
        {
            // 只等待物理键自然消失；禁止 DEL/EXPIRE、广播或 Hybrid 读取刷新制造过期。
            while (await database.KeyExistsAsync(entry.RedisKey))
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(entry.Clock.Elapsed < entry.ExpectedDuration + TimeSpan.FromSeconds(3),
                    $"{entry.Lifetime} Redis 物理键没有在实验窗口内过期。");
                await Task.Delay(50, token);
            }
            var expiredAt = entry.Clock.Elapsed;
            Assert.IsTrue(expiredAt >= entry.ExpectedDuration - TimeSpan.FromMilliseconds(1250)
                && expiredAt < entry.ExpectedDuration + TimeSpan.FromSeconds(3),
                "寿命观察必须同时满足下界和上界；过早丢失也不能视为通过。");
            Assert.AreEqual("refilled-current", await secondary.GetOrCreateAsync<string?>(entry.Key, _ =>
            {
                entry.ReadFactoryCalls++;
                return ValueTask.FromResult<string?>("refilled-current");
            }, entry.Options, cancellationToken: token));
            Assert.AreEqual(1, entry.ReadFactoryCalls, "物理过期后的第二实例必须只执行本次回源。");
            Assert.AreEqual("refilled-current", await primary.GetOrCreateAsync<string?>(entry.Key, _ =>
            {
                entry.ReadFactoryCalls++;
                return ValueTask.FromResult<string?>("unexpected-after-refill");
            }, entry.Options, cancellationToken: token));
            Assert.AreEqual(1, entry.ReadFactoryCalls, "回源结果必须写回 Redis 并被另一实例共享。");
            Assert.IsFalse((await primaryFusion.TryGetAsync<string?>(entry.Key, memoryProbe, token)).HasValue);
            Assert.IsFalse((await secondaryFusion.TryGetAsync<string?>(entry.Key, memoryProbe, token)).HasValue);
            if (entry.Lifetime == CacheEntryLifetime.Negative)
            {
                foreach (var normal in cases.Where(item => item.Lifetime == CacheEntryLifetime.Normal))
                {
                    Assert.IsTrue(await database.KeyExistsAsync(normal.RedisKey), "负缓存过期时正常缓存还必须存在。");
                    Assert.AreEqual(normal.OldValue, await secondary.GetOrCreateAsync<string?>(normal.Key, _ =>
                    {
                        normal.ReadFactoryCalls++;
                        return ValueTask.FromResult<string?>("unexpected-normal-expiry");
                    }, normal.Options, cancellationToken: token));
                    Assert.AreEqual(0, normal.ReadFactoryCalls);
                }
            }
            Console.WriteLine($"S0 Redis expiry: lifetime={entry.Lifetime}, factoryPrime={entry.PrimeFactoryCalls}, "
                + $"expiredAtSeconds={expiredAt.TotalSeconds:F3}, refillCalls={entry.ReadFactoryCalls}");
        }
    }

    private sealed class SharedExpirationCase
    {
        public required string Key { get; init; }
        public required CacheEntryLifetime Lifetime { get; init; }
        public required HybridCacheEntryOptions Options { get; init; }
        public required TimeSpan ExpectedDuration { get; init; }
        public string? OldValue { get; init; }
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public RedisKey RedisKey { get; set; }
        public int PrimeFactoryCalls { get; set; }
        public int ReadFactoryCalls { get; set; }
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
        // 这是检查实际 L1 的观察入口，不继承被测策略自身的禁读/禁写开关。
        options.SetSkipMemoryCache(false);
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
        string instanceId,
        bool shortSharedLifetime = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CacheOptions.SectionName}:RedisConnectionString"] = redisConnectionString,
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:OwnerModule"] = "demo",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:ConsistencyClass"] = "S0-L2",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:L2Duration"] = shortSharedLifetime ? "00:00:09" : "00:05:00",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:NegativeDuration"] = shortSharedLifetime ? "00:00:03" : "00:00:10",
                [$"{CacheOptions.SectionName}:Entries:{SharedL2Entry}:Jitter"] = "00:00:00",
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
