using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

[TestClass]
public sealed class CachePolicyRegistryTests
{
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
    public void S1_default_tenant_resolution_has_short_ttl_jitter_and_direct_invalidation()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions());
        var policy = registry.GetRequired(CacheEntryNames.TenantResolution);

        Assert.AreEqual("tenancy", policy.OwnerModule);
        Assert.AreEqual(CacheConsistencyClass.ImportantBusiness, policy.ConsistencyClass);
        Assert.AreEqual(TimeSpan.FromMinutes(5), policy.L1Duration);
        Assert.AreEqual(TimeSpan.FromMinutes(5), policy.L2Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(30), policy.Jitter);
        Assert.AreEqual(TimeSpan.FromMinutes(1), policy.NegativeDuration);
        Assert.IsTrue(policy.RequiresDirectInvalidation);
        Assert.IsFalse(policy.FailSafeEnabled);

        var access = registry.ResolveAccess(CacheEntryNames.TenantResolution);
        Assert.AreEqual(CacheAccessKind.UseCache, access.Kind);

        var options = registry.CreateEntryOptions(CacheEntryNames.TenantResolution);
        Assert.AreEqual(policy.L2Duration, options.Duration);
        Assert.AreEqual(policy.L1Duration, options.MemoryCacheDuration);
        Assert.AreEqual(policy.Jitter, options.JitterMaxDuration);
        Assert.IsFalse(options.IsFailSafeEnabled);
        Assert.IsFalse(options.SkipMemoryCacheRead);
    }

    [TestMethod]
    public void S2_allows_fail_safe_only_when_explicitly_enabled()
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
                    },
                },
            });

        var options = registry.CreateEntryOptions("demo.display");
        Assert.IsTrue(options.IsFailSafeEnabled);
    }

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
        Assert.AreEqual(TimeSpan.FromMinutes(15), policy.L1Duration);
        Assert.AreEqual(TimeSpan.FromDays(7), policy.L2Duration);
        Assert.IsFalse(policy.FailSafeEnabled);

        var options = registry.CreateHybridEntryOptions(CacheEntryNames.GridPreference);
        Assert.AreEqual(TimeSpan.FromDays(7), options.Expiration);
        Assert.AreEqual(TimeSpan.FromMinutes(15), options.LocalCacheExpiration);
    }

    [TestMethod]
    public void Hybrid_entry_options_distinguish_negative_and_normal_lifetimes()
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions());

        var negative = registry.CreateHybridEntryOptions(
            CacheEntryNames.TenantResolution,
            CacheEntryLifetime.Negative);
        var normal = registry.CreateHybridEntryOptions(CacheEntryNames.TenantResolution);

        Assert.AreEqual(TimeSpan.FromMinutes(1), negative.Expiration);
        Assert.AreEqual(TimeSpan.FromMinutes(1), negative.LocalCacheExpiration);
        Assert.AreEqual(TimeSpan.FromMinutes(5), normal.Expiration);
        Assert.AreEqual(TimeSpan.FromMinutes(5), normal.LocalCacheExpiration);
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
            CacheConsistencyClass.ImportantBusiness,
            registry.GetRequired(CacheEntryNames.TenantResolution).ConsistencyClass);
    }
}
