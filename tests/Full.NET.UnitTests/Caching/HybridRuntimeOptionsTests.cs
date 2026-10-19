using System.Text.Json;
using System.Diagnostics;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;


namespace Full.NET.UnitTests.Caching;

/// <summary>通过真实注册的 Hybrid 适配观察读写标志、抖动与共享写入完成时机。</summary>
[TestClass]
public sealed class HybridRuntimeOptionsTests
{
    [TestMethod]
    public async Task Registered_hybrid_flags_and_jitter_change_actual_layer_behavior()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var layer = new RecordingLayer();
        var token = timeout.Token;
        var observations = new List<object>();
        void Require(bool value, string message) { Assert.IsTrue(value, message); }
        ServiceProvider CreateProvider(string entryJitter = "00:00:00")
        {
            var services = new ServiceCollection();
            services.AddLogging(); services.AddSingleton<IDistributedCache>(layer);
            services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Jitter"] = "00:00:00",
                ["Cache:Entries:demo.flags:OwnerModule"] = "demo",
                ["Cache:Entries:demo.flags:ConsistencyClass"] = "S1",
                ["Cache:Entries:demo.flags:L1Duration"] = "00:01:00",
                ["Cache:Entries:demo.flags:L2Duration"] = "00:02:00",
                ["Cache:Entries:demo.flags:NegativeDuration"] = "00:00:01",
                ["Cache:Entries:demo.flags:Jitter"] = entryJitter,
            }).Build(), "Testing");
            return services.BuildServiceProvider();
        }
        string Key() => CacheKeyBuilder.ForGlobal("Testing", "demo", "flags", Guid.NewGuid().ToString("N"), "v1");
        await using var primary = CreateProvider();
        await using var secondary = CreateProvider();
        var cache = primary.GetRequiredService<HybridCache>();
        var remote = secondary.GetRequiredService<HybridCache>();
        var fusion = primary.GetRequiredService<IFusionCache>();
        var policy = primary.GetRequiredService<ICachePolicyRegistry>();
        var normal = policy.CreateHybridEntryOptions("demo.flags");
        HybridCacheEntryOptions Flags(HybridCacheEntryFlags flags) => new()
        { Expiration = normal.Expiration, LocalCacheExpiration = normal.LocalCacheExpiration, Flags = flags };
        var localProbe = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };
        localProbe.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);

        var key = Key();
        await cache.SetAsync(key, "baseline", normal, cancellationToken: token);
        Require((await fusion.TryGetAsync<string>(key, localProbe, token: token)).Value == "baseline", "正常调用未写入 L1");
        Require(await remote.GetOrCreateAsync(key, _ => ValueTask.FromResult("unexpected"), normal, cancellationToken: token) == "baseline", "正常调用未写入共享层");
        observations.Add(new { scenario = "baseline-local-and-shared", passed = true });

        key = Key();
        var writeFlag = HybridCacheEntryFlags.DisableDistributedCacheWrite;
        var writesBefore = layer.Writes;
        await cache.SetAsync(key, "local-only", Flags(writeFlag), cancellationToken: token);
        Require(layer.Writes == writesBefore, "禁写 L2 标志未阻止实际共享写入");
        Require((await fusion.TryGetAsync<string>(key, localProbe, token: token)).Value == "local-only", "禁写 L2 不应禁写 L1");
        observations.Add(new { scenario = "disable-distributed-write", sharedWrites = layer.Writes - writesBefore });

        key = Key();
        await remote.SetAsync(key, "remote-old", normal, cancellationToken: token);
        var readsBefore = layer.Reads;
        var factoryCalls = 0;
        var read = await cache.GetOrCreateAsync(key, _ => { factoryCalls++; return ValueTask.FromResult("factory-new"); },
            Flags(HybridCacheEntryFlags.DisableDistributedCacheRead), cancellationToken: token);
        Require(read == "factory-new" && factoryCalls == 1 && layer.Reads == readsBefore, "禁读 L2 标志未阻止共享旧值命中");
        observations.Add(new { scenario = "disable-distributed-read", sharedReads = layer.Reads - readsBefore, factoryCalls });

        key = Key();
        await fusion.SetAsync(key, "legacy-local", localProbe, token: token);
        await remote.SetAsync(key, "remote-new", normal, cancellationToken: token);
        var noLocal = Flags(HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite);
        factoryCalls = 0;
        read = await cache.GetOrCreateAsync(key, _ => { factoryCalls++; return ValueTask.FromResult("unexpected"); }, noLocal, cancellationToken: token);
        Require(read == "remote-new" && factoryCalls == 0, "禁读 L1 未绕过旧本地值");
        var local = await fusion.TryGetAsync<string>(key, localProbe, token: token);
        Require(!local.HasValue || local.Value == "legacy-local", "共享读取不应把新值回填 L1");
        key = Key();
        await cache.SetAsync(key, "shared-only", noLocal, cancellationToken: token);
        Require(!(await fusion.TryGetAsync<string>(key, localProbe, token: token)).HasValue, "禁写 L1 标志未生效");
        Require(await remote.GetOrCreateAsync(key, _ => ValueTask.FromResult("unexpected"), normal, cancellationToken: token) == "shared-only", "禁写 L1 不应禁写共享层");
        observations.Add(new { scenario = "disable-local-read-and-write", factoryCalls });

        key = Key(); factoryCalls = 0;
        var bypass = Flags(HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite
            | HybridCacheEntryFlags.DisableDistributedCacheRead | HybridCacheEntryFlags.DisableDistributedCacheWrite);
        readsBefore = layer.Reads; writesBefore = layer.Writes;
        for (var i = 0; i < 2; i++)
            Require(await cache.GetOrCreateAsync(key, _ => { factoryCalls++; return ValueTask.FromResult("authority"); }, bypass, cancellationToken: token) == "authority", "禁用缓存层仍应执行工厂");
        Require(factoryCalls == 2 && layer.Reads == readsBefore && layer.Writes == writesBefore, "组合标志仍使用了缓存层");
        observations.Add(new { scenario = "disable-both-layers", factoryCalls });

        key = Key();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        layer.Entered = entered; layer.Release = release;
        var pending = cache.SetAsync(key, "awaited-write", normal, cancellationToken: token).AsTask();
        try
        {
            await entered.Task.WaitAsync(token);
            Require(!pending.IsCompleted, "默认 Hybrid Set 提前返回，实际 L2 写入仍未完成");
        }
        finally
        {
            release.TrySetResult();
            try { await pending; } finally { layer.Entered = null; layer.Release = null; }
        }
        observations.Add(new { scenario = "shared-write-awaited-through-gate", passed = true });

        await using var jitterProvider = CreateProvider("00:00:01");
        var jitterCache = jitterProvider.GetRequiredService<HybridCache>();
        var jitterPolicy = jitterProvider.GetRequiredService<ICachePolicyRegistry>();
        foreach (var lifetime in new[] { CacheEntryLifetime.Normal, CacheEntryLifetime.Negative })
        {
            var original = jitterPolicy.CreateHybridEntryOptions("demo.flags", lifetime);
            var baseTtl = lifetime == CacheEntryLifetime.Normal ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(1);
            var options = new HybridCacheEntryOptions { Expiration = baseTtl, LocalCacheExpiration = baseTtl, Flags = original.Flags };
            var sharedLifetimes = new List<double>();
            for (var i = 0; i < 8; i++)
            {
                await jitterCache.SetAsync<string?>(Key(), lifetime == CacheEntryLifetime.Negative ? null : "jitter-value", options, cancellationToken: token);
                var actual = layer.LastTtl ?? throw new InvalidOperationException("未观察到实际共享层 TTL: " + JsonSerializer.Serialize(layer.LastOptions));
                // SDK 2.9.0 的物理 L2 到期不加抖动；L1 才使用 JitterMaxDuration。
                Require(actual >= baseTtl - TimeSpan.FromMilliseconds(50) && actual <= baseTtl + TimeSpan.FromMilliseconds(5), "物理 L2 TTL 不应被 L1 抖动延长");
                sharedLifetimes.Add(actual.TotalMilliseconds);
            }
            var localOnly = new HybridCacheEntryOptions { Expiration = baseTtl, LocalCacheExpiration = baseTtl,
                Flags = HybridCacheEntryFlags.DisableDistributedCacheRead | HybridCacheEntryFlags.DisableDistributedCacheWrite };
            var keys = Enumerable.Range(0, 16).Select(_ => Key()).ToArray();
            string? old = lifetime == CacheEntryLifetime.Negative ? null : "old";
            var seedWatch = Stopwatch.StartNew();
            foreach (var localKey in keys) await jitterCache.SetAsync<string?>(localKey, old, localOnly, cancellationToken: token);
            // 观察计时从全部写入完成之后开始，避免较晚写入的零抖动条目伪装成延期。
            var localWatch = Stopwatch.StartNew();
            var seedMs = seedWatch.Elapsed.TotalMilliseconds;
            var localFactoryCalls = 0;
            async Task<string?> ReadLocal(string localKey) => await jitterCache.GetOrCreateAsync<string?>(localKey, _ =>
            { localFactoryCalls++; return ValueTask.FromResult<string?>("new"); }, localOnly, cancellationToken: token);
            foreach (var localKey in keys) Require(await ReadLocal(localKey) == old, "L1 抖动实验初始值未命中");
            Require(localFactoryCalls == 0, "L1 阳性对照意外回源");
            var firstDeadline = baseTtl + TimeSpan.FromMilliseconds(80);
            if (localWatch.Elapsed < firstDeadline) await Task.Delay(firstDeadline - localWatch.Elapsed, token);
            var remainingOld = 0;
            var firstObservedMs = localWatch.Elapsed.TotalMilliseconds;
            Require(localWatch.Elapsed < baseTtl + TimeSpan.FromMilliseconds(600), "抖动实验调度过迟，不能观察延长窗口");
            foreach (var localKey in keys) if (await ReadLocal(localKey) == old) remainingOld++;
            Require(remainingOld > 0, "非零条目抖动没有进入实际 L1 写入");
            var finalDeadline = baseTtl + TimeSpan.FromSeconds(1.2);
            if (localWatch.Elapsed < finalDeadline) await Task.Delay(finalDeadline - localWatch.Elapsed, token);
            Require(localWatch.Elapsed < finalDeadline + TimeSpan.FromMilliseconds(200), "最终抖动观察调度过迟，不能证明观察窗口内收敛");
            foreach (var localKey in keys) Require(await ReadLocal(localKey) == "new", "L1 旧值超过 TTL 加最大抖动后仍未过期");
            Require(localWatch.Elapsed < finalDeadline + TimeSpan.FromMilliseconds(200), "最终抖动读取跨过观察期限");
            observations.Add(new { scenario = $"nonzero-L1-jitter-fixed-L2-{lifetime}", sharedLifetimes,
                remainingOld, seedMs, firstObservedMs, finalObservedMs = localWatch.Elapsed.TotalMilliseconds, localFactoryCalls });
        }
        Assert.AreEqual(8, observations.Count, "全部运行路径都必须执行到断言。");
    }

    private sealed class RecordingLayer : IDistributedCache, IDisposable
    {
        private readonly ServiceProvider _storage = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        private IDistributedCache Inner => _storage.GetRequiredService<IDistributedCache>();
        public int Reads; public int Writes;
        public TimeSpan? LastTtl;
        public DistributedCacheEntryOptions? LastOptions;
        public TaskCompletionSource? Entered; public TaskCompletionSource? Release;
        public byte[]? Get(string key) { Interlocked.Increment(ref Reads); return Inner.Get(key); }
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) { Interlocked.Increment(ref Reads); return await Inner.GetAsync(key, token); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        { Interlocked.Increment(ref Writes); Record(options); Inner.Set(key, value, options); }
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Entered?.TrySetResult(); if (Release is not null) await Release.Task.WaitAsync(token);
            Interlocked.Increment(ref Writes); Record(options);
            await Inner.SetAsync(key, value, options, token);
        }
        public void Refresh(string key) => Inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => Inner.RefreshAsync(key, token);
        public void Remove(string key) => Inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => Inner.RemoveAsync(key, token);
        public void Dispose() => _storage.Dispose();
        private void Record(DistributedCacheEntryOptions options)
        {
            LastOptions = options;
            LastTtl = options.AbsoluteExpirationRelativeToNow
                ?? (options.AbsoluteExpiration is { } absolute ? absolute - DateTimeOffset.UtcNow : null);
        }
    }
}
