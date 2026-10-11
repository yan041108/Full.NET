using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Serialization;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Full.NET.UnitTests.Caching;

/// <summary>直接覆盖 FusionCache 的真实 L2 包装与标签，不能只验证裸业务载荷序列化。</summary>
[TestClass]
public sealed class FusionCacheL2SerializerContractTests
{
    [TestMethod]
    [DataRow("tenant")]
    [DataRow("tenant-negative")]
    [DataRow("grid")]
    [DataRow("diagnostic")]
    public async Task Aot_and_jit_l2_entries_interoperate_and_tag_removal_remains_visible(string payload)
    {
        var serializer = new FullNetFusionCacheJsonSerializer(
        [new TenancyCacheJsonTypeInfoContributor(), new SettingsCacheJsonTypeInfoContributor(),
            new FusionCacheInfrastructureJsonTypeInfoContributor()]);
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var writer = CreateCache(distributed, serializer);
        using var reader = CreateCache(distributed, new FusionCacheSystemTextJsonSerializer());
        if (payload == "diagnostic")
        {
            var expected = new DiagnosticPolicyDocument(5, LoggingPressureState.Degraded,
                [new DiagnosticPolicyRule(DiagnosticPolicyScopeKind.Endpoint, "demo/{id}", 1.0,
                    null, 1024, 2048, DateTimeOffset.UtcNow.AddMinutes(30))]);
            await VerifyAsync(writer, reader, distributed, serializer, expected, (actual, value) =>
            {
                Assert.AreEqual(value.Version, actual.Version);
                Assert.AreEqual(value.PressureState, actual.PressureState);
                CollectionAssert.AreEqual(value.Rules.ToArray(), actual.Rules.ToArray());
            });
        }
        else if (payload == "grid")
        {
            var expected = new GridPreferenceResponse("identity.users", 1,
                [new GridColumnPreference("displayName", 0, 240, true, null)], 3);
            await VerifyAsync(writer, reader, distributed, serializer, expected, (actual, value) =>
            {
                Assert.AreEqual(value.GridKey, actual.GridKey);
                Assert.AreEqual(value.SchemaVersion, actual.SchemaVersion);
                Assert.AreEqual(value.Version, actual.Version);
                CollectionAssert.AreEqual(value.Columns.ToArray(), actual.Columns.ToArray());
            });
        }
        else
        {
            var expected = new TenantResolutionCacheEntry(payload == "tenant-negative" ? null :
                new TenantCachePayload(Guid.CreateVersion7(), "l2-proof", "租户缓存验收", "l2.example.test",
                    true, 7, "zh-CN", null, null, null));
            await VerifyAsync(writer, reader, distributed, serializer, expected,
                (actual, value) => Assert.AreEqual(value, actual));
        }
    }

    private static async Task VerifyAsync<T>(FusionCache writer, FusionCache reader, IDistributedCache distributed,
        FullNetFusionCacheJsonSerializer serializer, T expected, Action<T, T> assertValue)
    {
        const string key = "l2-proof", tag = "l2-proof-tag";
        var options = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(5), ReThrowSerializationExceptions = true };
        await writer.SetAsync(key, expected, options, [tag]);
        var jitRead = await reader.TryGetAsync<T>(key, options);
        Assert.IsTrue(jitRead.HasValue);
        assertValue(jitRead.Value, expected);

        // 用全新 L1 读取反向写入，证明现有 JIT 缓存格式可由静态序列化器消费。
        await reader.SetAsync(key, expected, options, [tag]);
        using var reverse = CreateCache(distributed, serializer);
        var nativeRead = await reverse.TryGetAsync<T>(key, options);
        Assert.IsTrue(nativeRead.HasValue);
        assertValue(nativeRead.Value, expected);
        await reverse.RemoveByTagAsync(tag, options);
        using var fresh = CreateCache(distributed, new FusionCacheSystemTextJsonSerializer());
        Assert.IsFalse((await fresh.TryGetAsync<T>(key, options)).HasValue);

        // 两个方向都从全新 L1 检查失效，避免把本地缓存命中误认为 L2 互通。
        await writer.SetAsync("jit-tag-key", expected, options, ["jit-tag"]);
        await reader.RemoveByTagAsync("jit-tag", options);
        using var afterJitTag = CreateCache(distributed, serializer);
        Assert.IsFalse((await afterJitTag.TryGetAsync<T>("jit-tag-key", options)).HasValue);

        await reader.SetAsync("aot-clear-key", expected, options);
        await reverse.ClearAsync(options: options);
        using var afterAotClear = CreateCache(distributed, new FusionCacheSystemTextJsonSerializer());
        Assert.IsFalse((await afterAotClear.TryGetAsync<T>("aot-clear-key", options)).HasValue);

        await writer.SetAsync("jit-clear-key", expected, options);
        await reader.ClearAsync(options: options);
        using var afterJitClear = CreateCache(distributed, serializer);
        Assert.IsFalse((await afterJitClear.TryGetAsync<T>("jit-clear-key", options)).HasValue);
    }

    private static FusionCache CreateCache(IDistributedCache distributed,
        ZiggyCreatures.Caching.Fusion.Serialization.IFusionCacheSerializer serializer)
    {
        var cache = new FusionCache(Options.Create(new FusionCacheOptions
        {
            CacheName = "l2-proof", RemoveByTagBehavior = RemoveByTagBehavior.Remove,
        }), new MemoryCache(new MemoryCacheOptions()));
        cache.SetupDistributedCache(distributed, serializer);
        return cache;
    }
}
