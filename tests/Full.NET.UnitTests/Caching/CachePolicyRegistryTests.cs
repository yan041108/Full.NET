using System.Diagnostics;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

/// <summary>缓存策略注册表：强一致类别禁用 L1。</summary>
[TestClass]
public sealed class CachePolicyRegistryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Explicit_zero_jitter_is_distinct_from_an_unspecified_override(bool explicitlyZero)
    {
        var definition = new CacheEntryDefinitionOptions
        {
            OwnerModule = "settings", ConsistencyClass = "S2",
            L1Duration = TimeSpan.FromMinutes(1), L2Duration = TimeSpan.FromMinutes(15),
        };
        if (explicitlyZero) definition.Jitter = TimeSpan.Zero;
        var registry = CachePolicyRegistry.Create(new CacheOptions
        {
            Jitter = TimeSpan.FromSeconds(30),
            Entries = { [CacheEntryNames.GridPreference] = definition },
        });
        var expected = explicitlyZero ? TimeSpan.Zero : TimeSpan.FromSeconds(30);
        Assert.AreEqual(expected, registry.GetRequired(CacheEntryNames.GridPreference).Jitter);
        Assert.AreEqual(expected, registry.CreateEntryOptions(CacheEntryNames.GridPreference).JitterMaxDuration);
    }

    [TestMethod]
    public void Configuration_binding_preserves_explicit_zero_jitter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Jitter"] = "00:00:30",
            ["Cache:Entries:settings.grid-preference:OwnerModule"] = "settings",
            ["Cache:Entries:settings.grid-preference:ConsistencyClass"] = "S2",
            ["Cache:Entries:settings.grid-preference:L1Duration"] = "00:01:00",
            ["Cache:Entries:settings.grid-preference:L2Duration"] = "00:15:00",
            ["Cache:Entries:settings.grid-preference:Jitter"] = "00:00:00",
        }).Build(), "Testing");
        using var provider = services.BuildServiceProvider();
        Assert.AreEqual(TimeSpan.Zero, provider.GetRequiredService<ICachePolicyRegistry>()
            .GetRequired(CacheEntryNames.GridPreference).Jitter);
    }

    [TestMethod]
    public void Default_grid_lifetimes_bound_missed_notifications_and_late_refill()
    {
        var policy = CachePolicyRegistry.Create(new CacheOptions()).GetRequired(CacheEntryNames.GridPreference);
        Assert.IsTrue(policy.L1Duration <= TimeSpan.FromMinutes(2));
        Assert.IsTrue(policy.L2Duration <= TimeSpan.FromMinutes(30));
    }

    [TestMethod]
    public void Version_recheck_without_registered_business_protocol_is_rejected()
    {
        Assert.ThrowsExactly<OptionsValidationException>(() => CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["demo.unverified"] = new CacheEntryDefinitionOptions
            {
                OwnerModule = "demo", ConsistencyClass = "S1", RequiresVersionRecheck = true,
                L1Duration = TimeSpan.FromSeconds(1), L2Duration = TimeSpan.FromSeconds(2),
            } },
        }));
    }

    [TestMethod]
    public void Tenant_admission_catalog_requires_authority_read()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions());
        Assert.AreEqual(CacheAccessKind.AuthorityRead,
            registry.ResolveAccess(CacheEntryNames.TenantResolution).Kind);
    }

    [TestMethod]
    public async Task S0_normal_and_negative_hybrid_options_ignore_existing_L1_and_do_not_refill_it()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["security.shared"] = new CacheEntryDefinitionOptions
            {
                OwnerModule = "identity", ConsistencyClass = "S0-L2",
                L2Duration = TimeSpan.FromMinutes(2), NegativeDuration = TimeSpan.FromSeconds(10),
            } },
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var fusion = provider.GetRequiredService<IFusionCache>();
        var memoryProbe = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };
        memoryProbe.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        foreach (var lifetime in new[] { CacheEntryLifetime.Normal, CacheEntryLifetime.Negative })
        {
            var legacyKey = $"security-{lifetime}";
            var newKey = $"new-security-{lifetime}";
            await cache.SetAsync(legacyKey, "old");
            // 探针允许读取 L1；先证明它能看到旧值，避免沿用 S0 禁读选项导致假阴性。
            var legacy = await fusion.TryGetAsync<string>(legacyKey, memoryProbe);
            Assert.IsTrue(legacy.HasValue);
            Assert.AreEqual("old", legacy.Value);
            var options = registry.CreateHybridEntryOptions("security.shared", lifetime);

            var value = await cache.GetOrCreateAsync(legacyKey, _ => ValueTask.FromResult("authority"), options);
            Assert.AreEqual("authority", value);
            Assert.AreEqual("old", (await fusion.TryGetAsync<string>(legacyKey, memoryProbe)).Value,
                "S0 工厂结果不能覆盖已存在的 L1。");

            await cache.SetAsync(newKey, "must-not-be-local", options);
            Assert.IsFalse((await fusion.TryGetAsync<string>(newKey, memoryProbe)).HasValue,
                "S0 Set 的正常值和负缓存都不能写入 L1；检查探针本身不得禁读 L1。");
            Assert.AreEqual("fresh", await cache.GetOrCreateAsync(newKey, _ => ValueTask.FromResult("fresh"), options));
            Assert.IsFalse((await fusion.TryGetAsync<string>(newKey, memoryProbe)).HasValue,
                "S0 GetOrCreate 的正常值和负缓存都不能回填 L1。");
            await AssertS0SharedLayerAsync(registry, lifetime);
        }
    }

    /// <summary>用两个独立 SDK 实例观察 S0 的共享读写；内存 L2 只替换存储，不模拟 Hybrid 行为。</summary>
    private static async Task AssertS0SharedLayerAsync(ICachePolicyRegistry registry, CacheEntryLifetime lifetime)
    {
        static ServiceProvider CreateProvider()
        {
            // 每节点独立注册 SDK 构建器；复用同一注册会返回同一缓存对象，不能证明跨实例行为。
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDistributedMemoryCache();
            services.AddFusionCache().WithSystemTextJsonSerializer().AsHybridCache();
            return services.BuildServiceProvider();
        }
        await using var primaryProvider = CreateProvider();
        await using var secondaryProvider = CreateProvider();
        var sharedLayer = primaryProvider.GetRequiredService<IDistributedCache>();
        var primaryFusion = primaryProvider.GetRequiredService<IFusionCache>();
        var secondaryFusion = secondaryProvider.GetRequiredService<IFusionCache>();
        primaryFusion.SetupDistributedCache(sharedLayer);
        secondaryFusion.SetupDistributedCache(sharedLayer);
        Assert.AreNotSame(primaryFusion, secondaryFusion);
        Assert.AreNotEqual(primaryFusion.InstanceId, secondaryFusion.InstanceId);
        var primary = primaryProvider.GetRequiredService<HybridCache>();
        var secondary = secondaryProvider.GetRequiredService<HybridCache>();
        var options = registry.CreateHybridEntryOptions("security.shared", lifetime);
        var memoryProbe = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };
        memoryProbe.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var key = $"shared-{lifetime}-{Guid.NewGuid():N}";
        var factoryCalls = 0;

        await secondaryFusion.SetAsync(key, "legacy-local", memoryProbe);
        Assert.AreEqual("legacy-local", (await secondaryFusion.TryGetAsync<string>(key, memoryProbe)).Value);
        await primary.SetAsync(key, "shared-v1", options);
        var value = await secondary.GetOrCreateAsync(key, _ =>
        {
            factoryCalls++;
            return ValueTask.FromResult("unexpected-factory");
        }, options);
        Assert.AreEqual("shared-v1", value, "S0 必须读取共享值，而不是已存在的旧 L1。");
        Assert.AreEqual(0, factoryCalls);
        Assert.IsFalse((await primaryFusion.TryGetAsync<string>(key, memoryProbe)).HasValue);
        var legacy = await secondaryFusion.TryGetAsync<string>(key, memoryProbe);
        if (legacy.HasValue) Assert.AreEqual("legacy-local", legacy.Value, "共享读取不得回填 L1。");

        // 两节点没有 Backplane；新共享值仍可见，证明结果不是旧 L1 命中或广播失效。
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
        Assert.AreEqual(1, factoryCalls, "第二个实例应命中共享层，不能再次执行工厂。");
        Assert.IsFalse((await primaryFusion.TryGetAsync<string>(factoryKey, memoryProbe)).HasValue);
        Assert.IsFalse((await secondaryFusion.TryGetAsync<string>(factoryKey, memoryProbe)).HasValue);
    }

    [TestMethod]
    [DataRow("999", null)]
    [DataRow("S1", "00:00:00")]
    [DataRow("S1", "-00:00:01")]
    public void Invalid_class_or_negative_lifetime_is_rejected(string consistency, string? negative)
    {
        Assert.ThrowsExactly<OptionsValidationException>(() => CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["demo.entry"] = new CacheEntryDefinitionOptions
            {
                OwnerModule = "demo", ConsistencyClass = consistency,
                L1Duration = TimeSpan.FromSeconds(10), L2Duration = TimeSpan.FromSeconds(20),
                NegativeDuration = negative is null ? null : TimeSpan.Parse(negative),
            } },
        }));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(67_108_865)]
    public void Entry_payload_budget_outside_supported_range_is_rejected(int bytes)
    {
        Assert.ThrowsExactly<OptionsValidationException>(() => CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["demo.entry"] = new CacheEntryDefinitionOptions
            {
                OwnerModule = "demo", ConsistencyClass = "S1",
                L1Duration = TimeSpan.FromSeconds(10), L2Duration = TimeSpan.FromSeconds(20),
                MaxSerializedBytes = bytes,
            } },
        }));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(67_108_864)]
    public void Entry_payload_budget_accepts_exact_supported_endpoints(int bytes)
    {
        var policies = CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["demo.entry"] = new CacheEntryDefinitionOptions
            {
                OwnerModule = "demo", ConsistencyClass = "S1",
                L1Duration = TimeSpan.FromSeconds(10), L2Duration = TimeSpan.FromSeconds(20),
                MaxSerializedBytes = bytes,
            } },
        });
        Assert.AreEqual(bytes, policies.GetRequired("demo.entry").MaxSerializedBytes);
    }

    [TestMethod]
    public void Unknown_entry_fails_at_startup_and_get_required()
    {
        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            CachePolicyRegistry.Create(
                new CacheOptions
                {
                    Entries =
                    {
                        ["demo.unknown-class"] = new CacheEntryDefinitionOptions
                        {
                            OwnerModule = "demo",
                            ConsistencyClass = "not-a-real-class",
                            L1Duration = TimeSpan.FromSeconds(1),
                            L2Duration = TimeSpan.FromSeconds(2),
                        },
                    },
                }));
        StringAssert.Contains(exception.Message, "ConsistencyClass");

        var registry = CachePolicyRegistry.Create(new CacheOptions());
        var missing = Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.GetRequired("missing.entry"));
        StringAssert.Contains(missing.Message, "Unknown cache entry");
    }

    [TestMethod]
    public void ListPolicies_returns_all_registered_entries_in_stable_order()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions());
        var policies = registry.ListPolicies();

        Assert.IsGreaterThanOrEqualTo(policies.Count, 3);
        Assert.IsTrue(
            policies.Zip(policies.OrderBy(policy => policy.EntryName, StringComparer.OrdinalIgnoreCase))
                .All(pair => pair.First.EntryName == pair.Second.EntryName));
        Assert.IsTrue(policies.Any(policy => policy.EntryName == CacheEntryNames.TenantResolution));
    }

    [TestMethod]
    public void S0_L2_disables_memory_cache()
    {
        var registry = CachePolicyRegistry.Create(
            new CacheOptions
            {
                Entries =
                {
                    ["security.shared"] = new CacheEntryDefinitionOptions
                    {
                        OwnerModule = "identity",
                        ConsistencyClass = "S0-L2",
                        L1Duration = TimeSpan.FromMinutes(1),
                        L2Duration = TimeSpan.FromMinutes(2),
                        Jitter = TimeSpan.FromSeconds(5),
                    },
                },
            });

        var options = registry.CreateEntryOptions("security.shared");
        Assert.IsTrue(options.SkipMemoryCacheRead);
        Assert.IsTrue(options.SkipMemoryCacheWrite);
        Assert.IsNull(options.MemoryCacheDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(2), options.Duration);
        Assert.IsFalse(options.IsFailSafeEnabled);
    }

    [TestMethod]
    public void C0_requires_authority_read_and_forbids_entry_options()
    {
        var registry = CachePolicyRegistry.Create(
            new CacheOptions
            {
                Entries =
                {
                    ["payments.balance"] = new CacheEntryDefinitionOptions
                    {
                        OwnerModule = "payments",
                        ConsistencyClass = "C0",
                        FailSafeEnabled = false,
                        RequiresVersionRecheck = true,
                    },
                },
            });

        var access = registry.ResolveAccess("payments.balance");
        Assert.AreEqual(CacheAccessKind.AuthorityRead, access.Kind);
        Assert.AreEqual(CacheConsistencyClass.AuthorityCritical, access.ConsistencyClass);
        Assert.IsFalse(registry.GetRequired("payments.balance").FailSafeEnabled);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.CreateEntryOptions("payments.balance"));
        StringAssert.Contains(exception.Message, "AuthorityRead");
    }

    [TestMethod]
    public void Tenant_admission_has_no_positive_or_negative_cache_lifetime()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions());
        var policy = registry.GetRequired(CacheEntryNames.TenantResolution);

        Assert.AreEqual("tenancy", policy.OwnerModule);
        Assert.AreEqual(CacheConsistencyClass.AuthorityCritical, policy.ConsistencyClass);
        Assert.AreEqual(TimeSpan.Zero, policy.L1Duration);
        Assert.AreEqual(TimeSpan.Zero, policy.L2Duration);
        Assert.AreEqual(TimeSpan.Zero, policy.Jitter);
        Assert.IsNull(policy.NegativeDuration);
        Assert.IsFalse(policy.FailSafeEnabled);

        var access = registry.ResolveAccess(CacheEntryNames.TenantResolution);
        Assert.AreEqual(CacheAccessKind.AuthorityRead, access.Kind);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.CreateEntryOptions(CacheEntryNames.TenantResolution));
    }

    [TestMethod]
    public async Task S2_allows_fail_safe_only_when_explicitly_enabled()
    {
        var rejected = Assert.ThrowsExactly<OptionsValidationException>(() =>
            CachePolicyRegistry.Create(
                new CacheOptions
                {
                    Entries =
                    {
                        ["demo.s1-failsafe"] = new CacheEntryDefinitionOptions
                        {
                            OwnerModule = "demo",
                            ConsistencyClass = "S1",
                            L1Duration = TimeSpan.FromSeconds(10),
                            L2Duration = TimeSpan.FromSeconds(20),
                            FailSafeEnabled = true,
                        },
                    },
                }));
        StringAssert.Contains(rejected.Message, "FailSafeEnabled");

        var registry = CachePolicyRegistry.Create(
            new CacheOptions
            {
                Entries =
                {
                    ["demo.display"] = new CacheEntryDefinitionOptions
                    {
                        OwnerModule = "demo",
                        ConsistencyClass = "S2",
                        L1Duration = TimeSpan.FromMinutes(1),
                        L2Duration = TimeSpan.FromMinutes(10),
                        FailSafeEnabled = true,
                        FailSafeMaxDuration = TimeSpan.FromMinutes(20),
                    },
                },
            });

        var options = registry.CreateEntryOptions("demo.display");
        Assert.IsTrue(options.IsFailSafeEnabled);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await AssertHybridFailSafeRetentionAsync(deadline.Token);
        await AssertHybridFailSafeRetentionAsync(deadline.Token, repeatSourceFailure: true);
    }

    /// <summary>观察 S2 工厂失败、连续降级读取和物理保留上限；仅覆盖单节点 JIT 内存路径。</summary>
    private static async Task AssertHybridFailSafeRetentionAsync(CancellationToken token, bool repeatSourceFailure = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Jitter"] = "00:00:00",
            ["Cache:Entries:demo.failsafe:OwnerModule"] = "demo",
            ["Cache:Entries:demo.failsafe:ConsistencyClass"] = "S2",
            ["Cache:Entries:demo.failsafe:L1Duration"] = "00:00:01",
            ["Cache:Entries:demo.failsafe:L2Duration"] = "00:00:01",
            ["Cache:Entries:demo.failsafe:Jitter"] = "00:00:00",
            ["Cache:Entries:demo.failsafe:FailSafeEnabled"] = "true",
            ["Cache:Entries:demo.failsafe:FailSafeMaxDuration"] = repeatSourceFailure ? "00:00:40" : "00:00:04",
        }).Build(), "Testing");
        await using var provider = services.BuildServiceProvider();
        var hybrid = provider.GetRequiredService<HybridCache>();
        var policies = provider.GetRequiredService<ICachePolicyRegistry>();
        var options = policies.CreateHybridEntryOptions("demo.failsafe");
        var key = CacheKeyBuilder.ForGlobal("Testing", "demo", "failsafe", Guid.NewGuid().ToString("N"), "v1");
        var watch = Stopwatch.StartNew();
        await hybrid.SetAsync(key, "display-old", options, cancellationToken: token);
        var failures = 0;
        async Task<string> ReadWithFailureAsync() => await hybrid.GetOrCreateAsync<string>(key, _ =>
        {
            failures++;
            return ValueTask.FromException<string>(new CacheSourceUnavailableException());
        }, options, cancellationToken: token);

        Assert.AreEqual("display-old", await ReadWithFailureAsync());
        Assert.AreEqual(0, failures, "先证明未过期命中，不以工厂未执行伪装故障降级。");
        await Task.Delay(TimeSpan.FromMilliseconds(1300), token);
        Assert.AreEqual("display-old", await ReadWithFailureAsync());
        Assert.AreEqual(1, failures, "必须实际执行并失败后才回退旧展示值。");
        // 默认 throttle 会抑制此窗口内的重试；连续读到旧值不能被误报为连续工厂故障。
        foreach (var seconds in new[] { 2.0, 2.7, 3.4 })
        {
            while (watch.Elapsed < TimeSpan.FromSeconds(seconds)) await Task.Delay(25, token);
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(seconds + 0.3),
                "重复降级读调度过晚，不能据此判断保留期限。");
            Assert.AreEqual("display-old", await ReadWithFailureAsync());
            Assert.AreEqual(1, failures, "默认 throttle 内应复用已确认的降级值，不能伪装成重复工厂故障。");
        }
        var expectedFailures = 2;
        if (repeatSourceFailure)
        {
            // 等默认三十秒 throttle 退出，确保原始保留期限内实际发生第二次工厂故障。
            while (watch.Elapsed < TimeSpan.FromSeconds(31.8)) await Task.Delay(25, token);
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(32.5), "第二次工厂故障调度过晚。");
            Assert.AreEqual("display-old", await ReadWithFailureAsync());
            Assert.AreEqual(2, failures, "必须观察到第二次实际源故障，不能用 throttle 命中代替重试。");
            expectedFailures = 3;
        }
        // 观察点留出过期调度余量；重复工厂失败不能将本场景的原始保留窗口滚动续期。
        var observeAfter = TimeSpan.FromSeconds(repeatSourceFailure ? 45 : 6);
        while (watch.Elapsed < observeAfter) await Task.Delay(50, token);
        Assert.IsTrue(watch.Elapsed < observeAfter + TimeSpan.FromSeconds(1), "保留期限退出观察调度过晚。");
        await Assert.ThrowsExactlyAsync<CacheSourceUnavailableException>(async () => await ReadWithFailureAsync());
        Assert.AreEqual(expectedFailures, failures, "保留期限退出后应重新尝试权威源，不能继续使用旧值。");
    }

    private sealed class CacheSourceUnavailableException : Exception;

    [TestMethod]
    public void N0_resolves_to_bypass_and_cannot_create_entry_options()
    {
        var registry = CachePolicyRegistry.Create(
            new CacheOptions
            {
                Entries =
                {
                    ["demo.raw"] = new CacheEntryDefinitionOptions
                    {
                        OwnerModule = "demo",
                        ConsistencyClass = "N0",
                    },
                },
            });

        var access = registry.ResolveAccess("demo.raw");
        Assert.AreEqual(CacheAccessKind.Bypass, access.Kind);
        Assert.AreEqual(CacheConsistencyClass.NotCached, access.ConsistencyClass);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.CreateEntryOptions("demo.raw"));
        StringAssert.Contains(exception.Message, "Bypass");
    }

    [TestMethod]
    public void S2_default_grid_preference_maps_hybrid_lifetimes()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions());
        var policy = registry.GetRequired(CacheEntryNames.GridPreference);

        Assert.AreEqual("settings", policy.OwnerModule);
        Assert.AreEqual(CacheConsistencyClass.DegradableDisplay, policy.ConsistencyClass);
        Assert.AreEqual(TimeSpan.FromMinutes(1), policy.L1Duration);
        Assert.AreEqual(TimeSpan.FromMinutes(15), policy.L2Duration);
        Assert.IsFalse(policy.FailSafeEnabled);

        var options = registry.CreateHybridEntryOptions(CacheEntryNames.GridPreference);
        Assert.AreEqual(TimeSpan.FromMinutes(15), options.Expiration);
        Assert.AreEqual(TimeSpan.FromMinutes(1), options.LocalCacheExpiration);
    }

    [TestMethod]
    public async Task Hybrid_entry_options_distinguish_negative_and_normal_lifetimes()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions { Entries = { ["demo.negative"] = new CacheEntryDefinitionOptions
        { OwnerModule = "demo", ConsistencyClass = "S1", L1Duration = TimeSpan.FromSeconds(30),
          L2Duration = TimeSpan.FromMinutes(2), NegativeDuration = TimeSpan.FromSeconds(15) } } });

        var negative = registry.CreateHybridEntryOptions(
            "demo.negative",
            CacheEntryLifetime.Negative);
        var normal = registry.CreateHybridEntryOptions("demo.negative");

        Assert.AreEqual(TimeSpan.FromSeconds(15), negative.Expiration);
        Assert.AreEqual(TimeSpan.FromSeconds(15), negative.LocalCacheExpiration);
        Assert.AreEqual(TimeSpan.FromMinutes(2), normal.Expiration);
        Assert.AreEqual(TimeSpan.FromSeconds(30), normal.LocalCacheExpiration);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        foreach (var consistency in new[] { "S0-L2", "S1", "S2" })
            await AssertHybridExpirationAsync(consistency, deadline.Token);
    }

    /// <summary>观察实际 SDK 的正值与 null 负缓存过期；共享内存层不能证明 Redis 或跨进程时效。</summary>
    private static async Task AssertHybridExpirationAsync(string consistency, CancellationToken token)
    {
        ServiceProvider CreateProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDistributedMemoryCache();
            services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Jitter"] = "00:00:00",
                ["Cache:Entries:demo.lifetime:OwnerModule"] = "demo",
                ["Cache:Entries:demo.lifetime:ConsistencyClass"] = consistency,
                ["Cache:Entries:demo.lifetime:L1Duration"] = consistency == "S0-L2" ? "00:00:00" : "00:00:02",
                ["Cache:Entries:demo.lifetime:L2Duration"] = "00:00:08",
                ["Cache:Entries:demo.lifetime:NegativeDuration"] = "00:00:01",
                ["Cache:Entries:demo.lifetime:Jitter"] = "00:00:00",
            }).Build(), "Testing");
            return services.BuildServiceProvider();
        }

        await using var primaryProvider = CreateProvider();
        await using var secondaryProvider = CreateProvider();
        var primaryFusion = primaryProvider.GetRequiredService<IFusionCache>();
        var secondaryFusion = secondaryProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotSame(primaryFusion, secondaryFusion);
        Assert.AreNotEqual(primaryFusion.InstanceId, secondaryFusion.InstanceId);
        var shared = primaryProvider.GetRequiredService<IDistributedCache>();
        primaryFusion.SetupDistributedCache(shared);
        secondaryFusion.SetupDistributedCache(shared);
        var primary = primaryProvider.GetRequiredService<HybridCache>();
        var secondary = secondaryProvider.GetRequiredService<HybridCache>();
        var policies = primaryProvider.GetRequiredService<ICachePolicyRegistry>();
        var normal = policies.CreateHybridEntryOptions("demo.lifetime");
        var negative = policies.CreateHybridEntryOptions("demo.lifetime", CacheEntryLifetime.Negative);
        var normalKey = CacheKeyBuilder.ForGlobal("Testing", "demo", "lifetime", Guid.NewGuid().ToString("N"), "v1");
        var negativeKey = CacheKeyBuilder.ForGlobal("Testing", "demo", "lifetime", Guid.NewGuid().ToString("N"), "v1");
        await primary.SetAsync(normalKey, "normal-old", normal, cancellationToken: token);
        await primary.SetAsync<string?>(negativeKey, null, negative, cancellationToken: token);
        var watch = Stopwatch.StartNew();
        var normalFactories = 0;
        var negativeFactories = 0;
        async Task<string> ReadNormalAsync() => await secondary.GetOrCreateAsync(normalKey, _ =>
        {
            normalFactories++;
            return ValueTask.FromResult("normal-new");
        }, normal, cancellationToken: token);
        async Task<string?> ReadNegativeAsync() => await secondary.GetOrCreateAsync(negativeKey, _ =>
        {
            negativeFactories++;
            return ValueTask.FromResult<string?>("negative-new");
        }, negative, cancellationToken: token);

        Assert.AreEqual("normal-old", await ReadNormalAsync());
        Assert.IsNull(await ReadNegativeAsync());
        Assert.AreEqual(0, normalFactories);
        Assert.AreEqual(0, negativeFactories, "必须先证明 null 已缓存，不能把未命中当作负缓存命中。");
        // 轮询观察到真正过期；固定上界用于拒绝沿用正常 TTL，不能只断言选项数值。
        while (await ReadNegativeAsync() is null && watch.Elapsed < TimeSpan.FromSeconds(4))
            await Task.Delay(50, token);
        Assert.AreEqual(1, negativeFactories, $"{consistency}: 负缓存未按短 TTL 过期。");
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(700), "负缓存不能明显早于配置的 1 秒寿命过期。");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(4));
        Assert.AreEqual("normal-old", await ReadNormalAsync(), "负缓存过期不能提前清理正常值。");
        Assert.AreEqual(0, normalFactories);
        while (await ReadNormalAsync() == "normal-old" && watch.Elapsed < TimeSpan.FromSeconds(12))
            await Task.Delay(50, token);
        Assert.AreEqual(1, normalFactories, $"{consistency}: 正常值未按 L2 TTL 过期。");
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromSeconds(7), "正常值不能错误地缩短为负缓存或 L1 的寿命。");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(12));
    }

    [TestMethod]
    public void Hybrid_entry_options_disable_local_cache_for_s0_l2()
    {
        var registry = CachePolicyRegistry.Create(
            new CacheOptions
            {
                Entries =
                {
                    ["security.shared"] = new CacheEntryDefinitionOptions
                    {
                        OwnerModule = "identity",
                        ConsistencyClass = "S0-L2",
                        L1Duration = TimeSpan.FromMinutes(1),
                        L2Duration = TimeSpan.FromMinutes(2),
                    },
                },
            });

        var options = registry.CreateHybridEntryOptions("security.shared");
        Assert.AreEqual(TimeSpan.FromMinutes(2), options.Expiration);
        Assert.AreEqual(TimeSpan.Zero, options.LocalCacheExpiration);
    }

    [TestMethod]
    public void AddFullNetCaching_registers_policy_registry_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Development");

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ICachePolicyRegistry>();
        Assert.AreSame(registry, provider.GetRequiredService<ICachePolicyRegistry>());
        Assert.AreEqual(
            CacheConsistencyClass.AuthorityCritical,
            registry.GetRequired(CacheEntryNames.TenantResolution).ConsistencyClass);
    }
}
