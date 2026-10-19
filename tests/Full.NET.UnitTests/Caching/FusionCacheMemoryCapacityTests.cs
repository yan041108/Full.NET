using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

/// <summary>验证载荷预算、宿主隔离及容量压力下的标签失效。</summary>
[TestClass]
public sealed class FusionCacheMemoryCapacityTests
{
    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("1000001")]
    public void Invalid_capacity_is_rejected_at_registration(string capacity)
    {
        Assert.ThrowsExactly<OptionsValidationException>(() => new ServiceCollection()
            .AddFullNetCaching(Configuration(capacity), "Testing"));
    }

    [TestMethod]
    public void Explicit_options_without_size_are_bounded_without_limiting_the_host_cache()
    {
        var services = new ServiceCollection().AddLogging().AddMemoryCache();
        services.AddFullNetCaching(Configuration("8"), "Testing");
        using var provider = services.BuildServiceProvider();
        var fusion = provider.GetRequiredService<IFusionCache>();
        var host = provider.GetRequiredService<IMemoryCache>();
        var options = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };
        for (var i = 0; i < 100; i++)
        {
            fusion.Set($"payload-{i}", i, options);
            host.Set($"host-{i}", i);
        }
        var retained = Enumerable.Range(0, 100).Count(i => fusion.TryGet<int>($"payload-{i}").HasValue);
        Assert.IsTrue(retained <= 8);
        Assert.AreEqual(100, Enumerable.Range(0, 100).Count(i => host.TryGetValue($"host-{i}", out _)));
    }

    [TestMethod]
    public async Task Hybrid_factory_results_survive_rejected_admission_and_tags_still_invalidate()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddFullNetCaching(Configuration("1"), "Testing");
        await using var provider = services.BuildServiceProvider();
        var fusion = provider.GetRequiredService<IFusionCache>();
        var hybrid = provider.GetRequiredService<HybridCache>();
        // 固定一个载荷占满预算，确保后续回源结果即使不能进入 L1 也正常返回。
        fusion.Set("pinned", 1, new FusionCacheEntryOptions
            { Duration = TimeSpan.FromMinutes(1), Priority = CacheItemPriority.NeverRemove }, tags: ["group"]);
        var calls = 0;
        for (var i = 0; i < 10; i++)
            Assert.AreEqual(42, await hybrid.GetOrCreateAsync<int>("overflow", _ =>
            {
                calls++;
                return ValueTask.FromResult(42);
            }));
        Assert.AreEqual(10, calls);
        await hybrid.RemoveByTagAsync("group");
        Assert.IsFalse((await fusion.TryGetAsync<int>("pinned")).HasValue);
    }

    private static IConfiguration Configuration(string capacity) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Cache:L1PayloadCapacity"] = capacity }).Build();
}
