using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>使用独占 ACL 用户验证真实 L2 故障、陈旧值上限和批量失效恢复，不停止共享 Redis。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheRedisFailureRecoveryTests
{
    private const string EntryName = "fixture.redis-failure";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Two_nodes_preserve_consistency_and_runtime_metrics_during_L2_failure_and_recovery(bool allowStale)
    {
        await using var acl = await RedisAcl.CreateAsync();
        using var metrics = new RuntimeMetrics();
        await using var primary = await Node.CreateAsync(acl, allowStale ? "S2" : "S0-L2");
        await using var peer = await Node.CreateAsync(acl, allowStale ? "S2" : "S0-L2");
        Assert.AreNotEqual(primary.Cache.InstanceId, peer.Cache.InstanceId);
        var options = primary.Options();
        var key = CacheKeyBuilder.ForGlobal("Testing", "fixture", "failure", acl.Suffix, "v1");
        var primed = Stopwatch.GetTimestamp();
        await primary.Cache.SetAsync(key, "old", options);
        var warm = await peer.Cache.TryGetAsync<string>(key, options);
        Assert.IsTrue(warm.HasValue, "故障注入前，第二节点必须实际读取尚未过期的共享条目。");
        Assert.AreEqual("old", warm.Value);
        await Task.Delay(options.Duration + TimeSpan.FromMilliseconds(150));
        // RedisCache 使用 Hash 命令；以命令类别覆盖 GET/HMGET/HGET 等实际读写路径。
        await acl.SetPermissionsAsync("-@read", "-@write");
        await AssertPermissionDeniedAsync(() => primary.Provider.GetRequiredService<IDistributedCache>().GetAsync(key));
        var calls = 0;
        foreach (var node in new[] { primary, peer })
        {
            var original = new InvalidOperationException("fixture-authority-unavailable");
            string? result = null;
            var failure = await CaptureAsync(async () => result = await node.Cache.GetOrSetAsync<string>(key,
                (_, _) => { Interlocked.Increment(ref calls); return Task.FromException<string>(original); }, options));
            if (allowStale)
            {
                Assert.IsNull(failure);
                Assert.AreEqual("old", result);
            }
            else
            {
                Assert.AreSame(original, failure, "S0-L2 不得用本机旧值或 Fail-Safe 掩盖权威源异常。");
            }
        }
        Assert.AreEqual(2, calls);
        await WaitAsync(() => metrics.Count("l2", "circuit", "open") >= 2
            && metrics.Count("factory", "foreground", "failure") >= 2);
        if (allowStale)
        {
            await WaitAsync(() => metrics.Count("cache", "fail_safe", "activated") >= 2);
            // 从初次写入起计时，不能把连续降级命中理解为无限延长保留期限。
            var remaining = options.FailSafeMaxDuration + TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(primed);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            var expiredSource = new InvalidOperationException("fixture-source-still-unavailable");
            var expired = await CaptureAsync(() => primary.Cache.GetOrSetAsync<string>(key,
                (_, _) => Task.FromException<string>(expiredSource), options).AsTask());
            Assert.AreSame(expiredSource, expired, "真实 L2 仍不可读时，超过保留上限必须结束陈旧值降级。");
        }
        else
        {
            Assert.AreEqual(0, metrics.Count("cache", "fail_safe", "activated"));
        }

        await acl.SetPermissionsAsync("+@read", "+@write");
        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await primary.Cache.SetAsync(key, "new", options);
        var onlyL1 = options.Duplicate();
        onlyL1.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        await peer.Cache.RemoveAsync(key, onlyL1);
        Assert.AreEqual("new", await peer.Cache.GetOrSetAsync<string>(key,
            (_, _) => Task.FromException<string>(new InvalidOperationException("恢复后应直接读取真实 L2。")), options));
        await WaitAsync(() => metrics.Count("l2", "circuit", "closed") >= 1);
        metrics.AssertNoPrivateLabels(key, acl.User, acl.Password);
    }

    [TestMethod]
    public async Task Batch_L2_remove_failure_clears_every_local_target_and_recovers_shared_and_peer_entries()
    {
        await using var acl = await RedisAcl.CreateAsync();
        using var metrics = new RuntimeMetrics();
        await using var primary = await Node.CreateAsync(acl, "S1");
        await using var peer = await Node.CreateAsync(acl, "S1");
        var options = primary.Options();
        var keys = Enumerable.Range(0, 2).Select(index =>
            CacheKeyBuilder.ForGlobal("Testing", "fixture", "batch", $"{acl.Suffix}:{index}", "v1")).ToArray();
        foreach (var key in keys)
        {
            await primary.Cache.SetAsync(key, "old", options);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options)).Value);
        }
        // SDK/客户端可选择 DEL 或 UNLINK；必须先证明真正的分布式删除已被拒绝。
        await acl.SetPermissionsAsync("-@write");
        await AssertPermissionDeniedAsync(() => primary.Provider.GetRequiredService<IDistributedCache>().RemoveAsync(keys[0]));
        var batch = primary.Provider.GetRequiredService<ICacheBatchInvalidator>();
        // 与提交后拥有者入口一致：先登记本机清理，再登记共享删除；SDK 故障仍须等待整批已启动目标。
        var targets = keys.Select(key => new CacheInvalidationTarget(EntryName, key, CacheInvalidationTargetKind.Key,
            CacheInvalidationScope.CurrentNodeOnly)).Concat(keys.Select(key => new CacheInvalidationTarget(EntryName, key,
            CacheInvalidationTargetKind.Key, CacheInvalidationScope.AllLayersSynchronous))).ToArray();
        var failure = await CaptureAsync(() => batch.RemoveAsync(targets).AsTask());
        Assert.IsNotNull(failure, "真实 Redis DEL 拒绝必须向拥有者报告失败，不能仅凭 L1 删除报告整批成功。");
        Assert.Contains("NOPERM", failure.ToString());
        var localOnly = options.Duplicate();
        localOnly.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        foreach (var key in keys)
            Assert.IsFalse((await primary.Cache.TryGetAsync<string>(key, localOnly)).HasValue);
        await WaitAsync(() => metrics.Measurements.Any(item => item.Name == "fullnet.cache.invalidation.batch.duration"
            && item.Tags.Any(tag => tag.Key == "outcome" && Equals(tag.Value, "failure"))));

        await acl.SetPermissionsAsync("+@write");
        await Task.Delay(TimeSpan.FromMilliseconds(350));
        await batch.RemoveAsync(targets);
        await WaitAsync(async () =>
        {
            foreach (var key in keys)
                if ((await peer.Cache.TryGetAsync<string>(key, localOnly)).HasValue) return false;
            return true;
        });
        await using var cold = await Node.CreateAsync(acl, "S1");
        foreach (var key in keys)
            Assert.IsFalse((await cold.Cache.TryGetAsync<string>(key, options)).HasValue,
                "恢复后的整批删除必须同时清理真实 L2，不能只观察已清空的本机 L1。");
        metrics.AssertNoPrivateLabels([.. keys, acl.User, acl.Password]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Timed_out_display_factory_finishes_in_background_and_reports_actual_result(bool fail)
    {
        await using var acl = await RedisAcl.CreateAsync();
        using var metrics = new RuntimeMetrics();
        await using var primary = await Node.CreateAsync(acl, "S2");
        await using var peer = await Node.CreateAsync(acl, "S2");
        var key = CacheKeyBuilder.ForGlobal("Testing", "fixture", "background", acl.Suffix, "v1");
        var options = primary.Options();
        options.FactorySoftTimeout = TimeSpan.FromMilliseconds(30);
        options.AllowTimedOutFactoryBackgroundCompletion = true;
        var factory = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await primary.Cache.SetAsync(key, "old", options);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options)).Value);
            await Task.Delay(options.Duration + TimeSpan.FromMilliseconds(150));
            Assert.AreEqual("old", await primary.Cache.GetOrSetAsync<string>(key, (_, _) => factory.Task, options));
            await WaitAsync(() => metrics.Count("factory", "foreground", "timeout") == 1
                && metrics.Count("cache", "fail_safe", "activated") >= 1);
            if (fail) factory.SetException(new InvalidOperationException("fixture-background-source-unavailable"));
            else factory.SetResult("new");
            await WaitAsync(() => metrics.Count("factory", "background", fail ? "failure" : "success") == 1);
            if (fail)
            {
                Assert.AreEqual(0, metrics.Count("factory", "background", "success"));
                Assert.AreEqual("old", await primary.Cache.GetOrSetAsync<string>(key,
                    (_, _) => Task.FromException<string>(new InvalidOperationException("fixture-source-unavailable")), options));
            }
            else
            {
                await WaitAsync(async () => (await peer.Cache.TryGetAsync<string>(key, options)).GetValueOrDefault() == "new");
                Assert.AreEqual("new", (await primary.Cache.TryGetAsync<string>(key, options)).Value);
                Assert.AreEqual(0, metrics.Count("factory", "background", "failure"));
            }
            metrics.AssertNoPrivateLabels(key, acl.User, acl.Password);
        }
        finally
        {
            // 即使前置断言失败也结束已启动工厂，避免把后台任务带出测试生命周期。
            factory.TrySetResult("cleanup");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Background_factory_success_with_L2_write_failure_keeps_local_result_and_recovers_shared_value(bool alsoDenyReads)
    {
        await using var acl = await RedisAcl.CreateAsync();
        using var metrics = new RuntimeMetrics();
        var jitter = TimeSpan.FromMilliseconds(100);
        await using var primary = await Node.CreateAsync(acl, "S2", jitter: jitter);
        await using var peer = await Node.CreateAsync(acl, "S2", jitter: jitter);
        var key = CacheKeyBuilder.ForGlobal("Testing", "fixture", "background-l2-failure", acl.Suffix, "v1");
        var options = primary.Options();
        options.Duration = TimeSpan.FromSeconds(2);
        options.MemoryCacheDuration = TimeSpan.FromSeconds(2);
        options.DistributedCacheDuration = TimeSpan.FromSeconds(2);
        options.FactorySoftTimeout = TimeSpan.FromMilliseconds(30);
        options.AllowTimedOutFactoryBackgroundCompletion = true;
        options.ReThrowDistributedCacheExceptions = false;
        Assert.AreEqual(jitter, options.JitterMaxDuration, "非零抖动必须来自实际注册策略。");
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var shared = options.Duplicate();
        shared.SkipMemoryCacheRead = true;
        shared.SkipMemoryCacheWrite = true;
        shared.IsFailSafeEnabled = false;
        var factory = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await primary.Cache.SetAsync(key, "old", options);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options)).Value);
            // 等过最大 L1 抖动后再触发故障；等待仅用于过期，工厂竞态仍由显式 gate 控制。
            await Task.Delay(options.Duration + options.JitterMaxDuration + TimeSpan.FromMilliseconds(150));
            Assert.AreEqual("old", await primary.Cache.GetOrSetAsync<string>(key, (_, _) => factory.Task, options));
            await WaitAsync(() => metrics.Count("factory", "foreground", "timeout") == 1);
            // 工厂已经转入后台后才拒绝写入，以真实 IDistributedCache 阳性对照证明 L2 故障。
            await acl.SetPermissionsAsync(alsoDenyReads ? ["-@read", "-@write"] : ["-@write"]);
            var distributed = primary.Provider.GetRequiredService<IDistributedCache>();
            await AssertPermissionDeniedAsync(() => distributed.SetAsync($"fixture-control:{acl.Suffix}", [1],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) }));
            if (alsoDenyReads) await AssertPermissionDeniedAsync(() => distributed.GetAsync(key));
            var released = Stopwatch.GetTimestamp();
            factory.SetResult("new-local");
            await WaitAsync(() => metrics.Count("factory", "background", "success") == 1
                && metrics.Count("l2", "circuit", "open") >= 1);
            Assert.AreEqual("new-local", (await primary.Cache.TryGetAsync<string>(key, local)).Value);
            Assert.IsTrue(Stopwatch.GetElapsedTime(released) < options.Duration,
                "本地观察不能发生在新值自然过期之后。");
            Assert.AreEqual(0, metrics.Count("factory", "background", "failure"),
                "源工厂成功与 L2 写入失败是不同事件，不能把后者伪报为源故障。");

            await acl.SetPermissionsAsync("+@read", "+@write");
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            var beforeRecovery = await peer.Cache.TryGetAsync<string>(key, shared);
            Assert.IsTrue(!beforeRecovery.HasValue || beforeRecovery.Value != "new-local",
                "后台写入失败且恢复队列关闭时，真实 L2 不应假装已经得到新值。");
            var recovered = options.Duplicate();
            recovered.Duration = TimeSpan.FromSeconds(10);
            recovered.MemoryCacheDuration = TimeSpan.FromSeconds(10);
            recovered.DistributedCacheDuration = TimeSpan.FromSeconds(10);
            await primary.Cache.SetAsync(key, "recovered-shared", recovered);
            await WaitAsync(async () => (await peer.Cache.TryGetAsync<string>(key, shared)).GetValueOrDefault() == "recovered-shared");
            await WaitAsync(() => metrics.Count("l2", "circuit", "closed") >= 1);
            metrics.AssertNoPrivateLabels(key, acl.User, acl.Password);
        }
        finally
        {
            // 所有失败路径都结束已启动工厂，并恢复独占用户权限，避免污染随后场景。
            factory.TrySetResult("cleanup");
            await acl.SetPermissionsAsync("+@read", "+@write");
        }
    }

    [TestMethod]
    public async Task Disconnected_peer_exits_old_L1_with_TTL_and_rejoins_actual_backplane()
    {
        await using var writerAcl = await RedisAcl.CreateAsync();
        await using var peerAcl = await RedisAcl.CreateAsync();
        await using var primary = await Node.CreateAsync(writerAcl, "S1");
        await using var peer = await Node.CreateAsync(peerAcl, "S1", application: writerAcl.Suffix);
        var options = primary.Options();
        options.Duration = TimeSpan.FromSeconds(3);
        options.MemoryCacheDuration = TimeSpan.FromSeconds(3);
        options.DistributedCacheDuration = TimeSpan.FromSeconds(30);
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var key = CacheKeyBuilder.ForGlobal("Testing", "fixture", "disconnect", writerAcl.Suffix, "v1");
        await primary.Cache.SetAsync(key, "old", options);
        Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options)).Value);
        await peerAcl.DisconnectAsync();
        await primary.Cache.SetAsync(key, "new", options);
        Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, local)).Value,
            "真正断连的节点应保留旧 L1；此阳性对照排除广播仍到达的伪故障。" );
        // 覆盖多个重连尝试及完整 L1 TTL；按实际时长报告，不外推分钟级断网。
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.IsFalse((await peer.Cache.TryGetAsync<string>(key, local)).HasValue);
        await peerAcl.SetPermissionsAsync("on");
        await WaitAsync(async () => (await peer.Cache.TryGetAsync<string>(key, options)).GetValueOrDefault() == "new");
        // L2 连接恢复不等于订阅恢复；用实际 SDK 收信和长 TTL 条目排除自然过期造成的假阳性。
        var received = 0;
        peer.Cache.Events.Backplane.MessageReceived += (_, args) =>
        {
            if (args.Message.SourceId == primary.Cache.InstanceId
                && args.Message.CacheKey?.EndsWith(key, StringComparison.Ordinal) == true)
                Interlocked.Increment(ref received);
        };
        var rejoined = options.Duplicate();
        rejoined.Duration = TimeSpan.FromSeconds(30);
        rejoined.MemoryCacheDuration = TimeSpan.FromSeconds(30);
        // 本地阳性对照的 TTL 长于等待窗口，确保订阅恢复不能靠条目自然过期蒙混通过。
        var rejoinedLocal = rejoined.Duplicate();
        rejoinedLocal.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        await peer.Cache.SetAsync(key, "new", rejoinedLocal);
        Assert.AreEqual("new", (await peer.Cache.TryGetAsync<string>(key, rejoinedLocal)).Value);
        await WaitAsync(async () =>
        {
            await primary.Cache.SetAsync(key, "resubscribed", rejoined);
            // SDK 在处理 L1 前触发收信事件，必须同时等待缓存的实际可见值。
            return Volatile.Read(ref received) > 0
                && (await peer.Cache.TryGetAsync<string>(key, rejoined)).GetValueOrDefault() == "resubscribed";
        });
        Assert.AreEqual("resubscribed", (await peer.Cache.TryGetAsync<string>(key, rejoined)).Value);
        var beforeRemove = Volatile.Read(ref received);
        await primary.Cache.RemoveAsync(key, rejoined);
        await WaitAsync(async () => Volatile.Read(ref received) > beforeRemove
            && !(await peer.Cache.TryGetAsync<string>(key, local)).HasValue);
    }

    [TestMethod]
    public async Task Recovery_queue_caps_pending_targets_replays_retained_entries_and_uses_TTL_for_evicted_entries()
    {
        await using var acl = await RedisAcl.CreateAsync();
        var recovery = new RecoveryLog();
        await using var primary = await Node.CreateAsync(acl, "S1", ConfigureRecovery, recovery);
        await using var peer = await Node.CreateAsync(acl, "S1");
        var options = primary.Options();
        options.Duration = TimeSpan.FromSeconds(10);
        options.MemoryCacheDuration = TimeSpan.FromSeconds(10);
        options.ReThrowBackplaneExceptions = false;
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var keys = Enumerable.Range(0, 8).Select(index =>
            CacheKeyBuilder.ForGlobal("Testing", "fixture", "queue", $"{acl.Suffix}:{index}", "v1")).ToArray();
        foreach (var key in keys)
        {
            await primary.Cache.SetAsync(key, "old", options);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options)).Value);
        }
        await acl.SetPermissionsAsync("-publish");
        await acl.AssertPublishDeniedAsync();
        foreach (var key in keys) await primary.Cache.RemoveAsync(key, options);
        Assert.IsTrue(recovery.MaxSizeReached, "超过上限必须实际触发 SDK 队列容量分支。" );
        foreach (var key in keys) Assert.IsTrue((await peer.Cache.TryGetAsync<string>(key, local)).HasValue);
        await Task.Delay(TimeSpan.FromMilliseconds(2500));
        Assert.IsTrue(recovery.PendingCounts.Count > 0, "持续故障期间必须实际尝试恢复。" );
        Assert.IsTrue(recovery.PendingCounts.All(count => count is > 0 and <= 3));
        await acl.SetPermissionsAsync("+publish");
        await WaitAsync(() => recovery.CompletedCounts.Count > 0);
        Assert.IsTrue(recovery.CompletedCounts.All(count => count is > 0 and <= 3));
        var removed = 0;
        foreach (var key in keys) if (!(await peer.Cache.TryGetAsync<string>(key, local)).HasValue) removed++;
        Assert.IsTrue(removed is > 0 and <= 3, "恢复只重放有界队列保留项，不能宣称全部失败消息已送达。" );
        await Task.Delay(TimeSpan.FromSeconds(10));
        foreach (var key in keys) Assert.IsFalse((await peer.Cache.TryGetAsync<string>(key, options)).HasValue,
            "被队列淘汰的失效目标仍必须通过 TTL 结束旧 L1，且真实 L2 已删除。" );
    }

    [TestMethod]
    public async Task Shutdown_cancels_pending_recovery_without_claiming_reliable_drain()
    {
        await using var acl = await RedisAcl.CreateAsync();
        var recovery = new RecoveryLog();
        await using var peer = await Node.CreateAsync(acl, "S1");
        var primary = await Node.CreateAsync(acl, "S1", ConfigureRecovery, recovery);
        var options = primary.Options();
        options.ReThrowBackplaneExceptions = false;
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var key = CacheKeyBuilder.ForGlobal("Testing", "fixture", "shutdown", acl.Suffix, "v1");
        var receivedRemovals = 0;
        var instance = primary.Cache.InstanceId;
        peer.Cache.Events.Backplane.MessageReceived += (_, args) =>
        {
            if (args.Message.SourceId == instance && args.Message.Action.ToString() == "EntryRemove"
                && args.Message.CacheKey?.EndsWith(key, StringComparison.Ordinal) == true)
                Interlocked.Increment(ref receivedRemovals);
        };
        try
        {
            await primary.Cache.SetAsync(key, "old", options);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options)).Value);
            await acl.SetPermissionsAsync("-publish");
            await acl.AssertPublishDeniedAsync();
            await primary.Cache.RemoveAsync(key, options);
            await WaitAsync(() => recovery.PendingCounts.Count > 0);
        }
        finally { await primary.DisposeAsync(); }
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        var attempts = recovery.PendingCounts.Count;
        await acl.SetPermissionsAsync("+publish");
        await Task.Delay(TimeSpan.FromMilliseconds(2500));
        Assert.AreEqual(attempts, recovery.PendingCounts.Count, "宿主关闭后不能继续重试共享 Redis。" );
        // SDK 关闭期间可能记录空操作完成；只以实际收信和未过期的远端 L1 判断是否送达。
        Assert.AreEqual(0, Volatile.Read(ref receivedRemovals), "关闭取消不等于待处理消息成功排空。" );
        Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, local)).Value);
    }

    private static void ConfigureRecovery(FusionCacheOptions options)
    {
        options.EnableAutoRecovery = true;
        options.AutoRecoveryDelay = TimeSpan.FromSeconds(2);
        options.BackplaneCircuitBreakerDuration = TimeSpan.FromMilliseconds(200);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception exception) { return exception; }
    }

    private static async Task AssertPermissionDeniedAsync(Func<Task> action)
    {
        var failure = await CaptureAsync(action);
        Assert.IsNotNull(failure, "故障阳性对照必须在真实 IDistributedCache 协议上失败。");
        Assert.Contains("NOPERM", failure.ToString());
    }

    private static Task WaitAsync(Func<bool> condition) => WaitAsync(() => Task.FromResult(condition()));

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(25, deadline.Token);
    }

    private sealed class Node(ServiceProvider provider) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public IFusionCache Cache => Provider.GetRequiredService<IFusionCache>();

        public static async Task<Node> CreateAsync(RedisAcl acl, string consistency,
            Action<FusionCacheOptions>? configure = null, RecoveryLog? recovery = null, string? application = null,
            TimeSpan? jitter = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            if (recovery is not null) services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recovery));
            var display = consistency == "S2";
            // RedisCache 的物理 TTL 取整到秒；S0 留出连接和跨节点预热余量，避免一秒被取整为零。
            var lifetime = consistency == "S1" ? "00:00:30" : consistency == "S0-L2" ? "00:00:03" : "00:00:01";
            services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisConnectionString"] = acl.Connection,
                ["Cache:ApplicationName"] = $"recovery-{application ?? acl.Suffix}",
                ["Cache:AutoRecoveryMaxItems"] = "3",
                ["Cache:AutoRecoveryMaxRetryCount"] = "3",
                [$"Cache:Entries:{EntryName}:OwnerModule"] = "fixture",
                [$"Cache:Entries:{EntryName}:ConsistencyClass"] = consistency,
                [$"Cache:Entries:{EntryName}:L1Duration"] = lifetime,
                [$"Cache:Entries:{EntryName}:L2Duration"] = lifetime,
                [$"Cache:Entries:{EntryName}:Jitter"] = (jitter ?? TimeSpan.Zero).ToString("c", System.Globalization.CultureInfo.InvariantCulture),
                [$"Cache:Entries:{EntryName}:FailSafeEnabled"] = display.ToString(),
                [$"Cache:Entries:{EntryName}:FailSafeMaxDuration"] = display ? "00:00:05" : null,
            }).Build(), "Testing");
            services.PostConfigure<FusionCacheOptions>(options =>
            {
                options.WaitForInitialBackplaneSubscribe = true;
                options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromMilliseconds(200);
                options.EnableAutoRecovery = false;
                configure?.Invoke(options);
            });
            var result = new Node(services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
            try
            {
                _ = result.Cache;
                foreach (var monitor in result.Provider.GetServices<IHostedService>()) await monitor.StartAsync(CancellationToken.None);
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }

        public FusionCacheEntryOptions Options()
        {
            var options = Provider.GetRequiredService<ICachePolicyRegistry>().CreateEntryOptions(EntryName);
            options.AllowBackgroundDistributedCacheOperations = false;
            options.AllowBackgroundBackplaneOperations = false;
            options.FailSafeThrottleDuration = TimeSpan.FromMilliseconds(100);
            return options;
        }

        public async ValueTask DisposeAsync()
        {
            try { foreach (var monitor in Provider.GetServices<IHostedService>()) await monitor.StopAsync(CancellationToken.None); }
            finally { await Provider.DisposeAsync(); }
        }
    }

    private sealed class RedisAcl(ConnectionMultiplexer admin, string suffix, string user, string password, string connection) : IAsyncDisposable
    {
        public string Suffix { get; } = suffix;
        public string User { get; } = user;
        public string Password { get; } = password;
        public string Connection { get; } = connection;

        public static async Task<RedisAcl> CreateAsync()
        {
            var configuration = ConfigurationOptions.Parse(await SharedDatabaseFixture.GetRedisConnectionStringAsync());
            configuration.AllowAdmin = true;
            var admin = await ConnectionMultiplexer.ConnectAsync(configuration);
            var suffix = Guid.NewGuid().ToString("N");
            var user = $"fullnet-cache-recovery-{suffix}";
            var password = Guid.NewGuid().ToString("N");
            try
            {
                await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", user, "reset", "on", $">{password}", "~*", "&*", "+@all");
                configuration.User = user; configuration.Password = password;
                return new RedisAcl(admin, suffix, user, password, configuration.ToString(includePassword: true));
            }
            catch { await admin.DisposeAsync(); throw; }
        }

        public async Task SetPermissionsAsync(params string[] rules) =>
            await admin.GetDatabase().ExecuteAsync("ACL", ["SETUSER", User, .. rules.Cast<object>()]);

        public async Task DisconnectAsync()
        {
            await SetPermissionsAsync("off");
            var killed = (long)await admin.GetDatabase().ExecuteAsync("CLIENT", "KILL", "USER", User);
            Assert.IsTrue(killed > 0, "断网对照必须实际关闭该独占用户的 Redis 连接。" );
        }

        public async Task AssertPublishDeniedAsync()
        {
            using var client = await ConnectionMultiplexer.ConnectAsync(Connection);
            await AssertPermissionDeniedAsync(() => client.GetSubscriber().PublishAsync(RedisChannel.Literal($"fixture:{Suffix}"), "control"));
        }

        public async ValueTask DisposeAsync()
        {
            try { await admin.GetDatabase().ExecuteAsync("ACL", "DELUSER", User); }
            finally { await admin.DisposeAsync(); }
        }
    }

    // 只采集 SDK 的容量和尝试数量，不保存其日志中的业务键、临时用户和凭据。
    private sealed class RecoveryLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<int> PendingCounts { get; } = new();
        public ConcurrentQueue<int> CompletedCounts { get; } = new();
        public bool MaxSizeReached => Volatile.Read(ref _maxSizeReached) != 0;
        private int _maxSizeReached;
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var values = fields.ToDictionary(item => item.Key, item => item.Value);
            var template = values.GetValueOrDefault("{OriginalFormat}") as string;
            if (template?.Contains("queue has reached the max size", StringComparison.Ordinal) == true)
                Interlocked.Exchange(ref _maxSizeReached, 1);
            if (values.GetValueOrDefault("Count") is not int count) return;
            if (template?.Contains("starting auto-recovery of", StringComparison.Ordinal) == true) PendingCounts.Enqueue(count);
            if (template?.Contains("completed auto-recovery of", StringComparison.Ordinal) == true) CompletedCounts.Enqueue(count);
        }
    }

    private sealed class RuntimeMetrics : IDisposable
    {
        private readonly MeterListener _listener = new();
        public ConcurrentQueue<Measurement> Measurements { get; } = new();

        public RuntimeMetrics()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CacheReliabilityTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Measurements.Enqueue(new(instrument.Name, tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Measurements.Enqueue(new(instrument.Name, tags.ToArray())));
            _listener.Start();
        }

        public int Count(string layer, string operation, string result) => Measurements.Count(item =>
            item.Name == "fullnet.cache.runtime.events" && item.Tags.Any(tag => tag.Key == "layer" && Equals(tag.Value, layer))
            && item.Tags.Any(tag => tag.Key == "operation" && Equals(tag.Value, operation))
            && item.Tags.Any(tag => tag.Key == "result" && Equals(tag.Value, result)));

        public void AssertNoPrivateLabels(params string[] values)
        {
            Assert.IsFalse(Measurements.SelectMany(item => item.Tags).Any(tag => values.Contains(tag.Value as string)),
                "真实故障指标不得包含缓存键、临时用户或凭据。");
            Assert.IsFalse(Measurements.SelectMany(item => item.Tags).Any(tag => tag.Key is "key" or "tenant" or "exception"));
        }

        public void Dispose() => _listener.Dispose();
        public sealed record Measurement(string Name, KeyValuePair<string, object?>[] Tags);
    }
}
