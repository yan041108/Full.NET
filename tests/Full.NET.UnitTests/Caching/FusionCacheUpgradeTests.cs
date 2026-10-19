using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Caching;

/// <summary>SDK 升级回归：硬清空后不得从已过期的 L2 恢复 Fail-Safe 旧值。</summary>
[TestClass]
public sealed class FusionCacheUpgradeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Clear_respects_fail_safe_permission_for_a_logically_expired_L2_fallback(bool allowFailSafe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Testing");
        services.PostConfigure<FusionCacheOptions>(options => options.ReThrowOriginalExceptions = true);
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IFusionCache>();
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        // SDK 自动注册会忽略内存 L2；此处显式接入替身，保留项目实际注册的序列化器。
        cache.SetupDistributedCache(distributed);
        var options = new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromMilliseconds(100),
            DistributedCacheDuration = TimeSpan.FromMilliseconds(100),
            JitterMaxDuration = TimeSpan.Zero,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(1),
            SkipMemoryCacheRead = true,
            SkipMemoryCacheWrite = true,
            AllowBackgroundDistributedCacheOperations = false,
        };
        var key = $"upgrade-clear-{Guid.NewGuid():N}";
        await cache.SetAsync(key, 42, options);
        // 时间等待仅覆盖逻辑过期；物理保留一分钟，源故障时原本仍可降级。
        await Task.Delay(300);
        await cache.ClearAsync(allowFailSafe: allowFailSafe);
        var original = new InvalidOperationException("authority unavailable");
        if (allowFailSafe)
        {
            Assert.AreEqual(42, await cache.GetOrSetAsync<int>(key,
                (_, _) => Task.FromException<int>(original), options));
            return;
        }
        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            cache.GetOrSetAsync<int>(key, (_, _) => Task.FromException<int>(original), options).AsTask());
        Assert.AreSame(original, thrown);
    }
}
