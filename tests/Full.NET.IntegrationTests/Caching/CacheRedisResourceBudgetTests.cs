using System.Collections.Concurrent;
using System.Diagnostics;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>真实 Redis 的批次接纳、共同并发和持续断连实验；不把本地规模外推为生产容量。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheRedisResourceBudgetTests
{
    private const string EntryName = "fixture.resources";
    private const int Concurrency = 4;
    private const int RecoveryLimit = 16;

    /// <summary>只输出数量、时间和进程资源观测，不记录临时 Redis 用户、凭据或业务键。</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Concurrent_batches_remove_256_real_targets_with_shared_slots_and_reject_third_batch()
    {
        await using var acl = await RedisUser.CreateAsync();
        var operations = new OperationGate();
        await using var primary = await Node.CreateAsync(acl, acl.Suffix, operations);
        await using var peer = await Node.CreateAsync(acl, acl.Suffix);
        var options = primary.Options(TimeSpan.FromMinutes(5));
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        var keys = Keys(acl.Suffix, 256);
        foreach (var key in keys)
        {
            await primary.Cache.SetAsync(key, "old", options, token: token);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options, token)).Value);
        }

        var received = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        peer.Cache.Events.Backplane.MessageReceived += (_, args) =>
        {
            if (args.Message.SourceId != primary.Cache.InstanceId || args.Message.Action.ToString() != "EntryRemove") return;
            var key = keys.FirstOrDefault(key => args.Message.CacheKey?.EndsWith(key, StringComparison.Ordinal) == true);
            if (key is not null) received.TryAdd(key, 0);
        };
        var batch = primary.Provider.GetRequiredService<ICacheBatchInvalidator>();
        var targets = keys.Select(Target).ToArray();
        var startedAt = Stopwatch.GetTimestamp();
        var pending = Task.WhenAll(batch.RemoveAsync(targets[..128], token).AsTask(),
            batch.RemoveAsync(targets[128..], token).AsTask());
        try
        {
            // 四个实际 SDK 操作全部被 gate 持有，两批保持接纳状态，第三批不得等待或枚举输入。
            await operations.FourStarted.Task.WaitAsync(token);
            Assert.AreEqual(Concurrency, operations.Active);
            var enumerated = false;
            IEnumerable<CacheInvalidationTarget> RejectedTargets()
            {
                enumerated = true;
                yield return targets[0];
            }
            var rejected = batch.RemoveAsync(RejectedTargets(), token).AsTask();
            Assert.IsTrue(rejected.IsCompleted, "容量拒绝不能排入等待队列。");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => rejected);
            Assert.IsFalse(enumerated);
            operations.Release.TrySetResult();
            await pending;
            Assert.AreEqual(256, operations.Calls);
            Assert.AreEqual(Concurrency, operations.Peak);
            Assert.AreEqual(0, operations.Active);
            await WaitAsync(() => Task.FromResult(received.Count == keys.Length), token);
            foreach (var key in keys)
                Assert.IsFalse((await peer.Cache.TryGetAsync<string>(key, local, token)).HasValue,
                    "五分钟寿命的远端 L1 必须实际失效，不能等待自然过期冒充广播成功。");
            // 新实例没有任何 L1；必须同时证明真实共享条目已经删除。
            await using var cold = await Node.CreateAsync(acl, acl.Suffix);
            foreach (var key in keys)
                Assert.IsFalse((await cold.Cache.TryGetAsync<string>(key, options, token)).HasValue);
            TestContext.WriteLine("targets=256 batches=2 peakSdkOperations={0} receivedRemovals={1} elapsedMs={2:F0}",
                operations.Peak, received.Count, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        finally
        {
            operations.Release.TrySetResult();
            // 失败断言也必须观察已启动批次；保留首先传播的异常，不遗留无人观察任务。
            try { await pending; } catch (Exception) { }
        }
    }

    [TestMethod]
    public async Task Thirty_second_disconnect_observes_recovery_budget_TTL_exit_and_actual_resubscription()
    {
        await using var writer = await RedisUser.CreateAsync();
        await using var reader = await RedisUser.CreateAsync();
        var recovery = new RecoveryLog();
        await using var primary = await Node.CreateAsync(writer, writer.Suffix, recovery: recovery);
        await using var peer = await Node.CreateAsync(reader, writer.Suffix);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        var options = primary.Options(TimeSpan.FromSeconds(3));
        options.DistributedCacheDuration = TimeSpan.FromMinutes(2);
        var local = options.Duplicate();
        local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
        var keys = Keys(writer.Suffix, 128);
        foreach (var key in keys)
        {
            await primary.Cache.SetAsync(key, "old", options, token: token);
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(key, options, token)).Value);
        }
        try
        {
            await reader.DisconnectAsync();
            await writer.SetPermissionsAsync("-publish");
            await writer.AssertPublishDeniedAsync();
            Assert.AreEqual("old", (await peer.Cache.TryGetAsync<string>(keys[^1], local, token)).Value,
                "断连阳性对照须发生在 L1 过期前。");
            var failure = await CaptureAsync(() => primary.Provider.GetRequiredService<ICacheBatchInvalidator>()
                .RemoveAsync(keys.Select(Target), token).AsTask());
            Assert.IsNotNull(failure, "真实发布失败应由同步失效入口传播，不能伪造成功。");
            Assert.IsTrue(recovery.MaxSizeReached, "128 目标必须实际进入 SDK 恢复队列容量分支。");
            var startedAt = Stopwatch.GetTimestamp();
            long peakManagedBytes = 0, peakWorkingSetBytes = 0;
            var samples = 0;
            using var process = Process.GetCurrentProcess();
            // 持续跨越十个 L1 生命周期及多个恢复尝试；资源值是整个测试进程观测，不作独占缓存内存保证。
            while (Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(30))
            {
                peakManagedBytes = Math.Max(peakManagedBytes, GC.GetTotalMemory(forceFullCollection: false));
                process.Refresh();
                peakWorkingSetBytes = Math.Max(peakWorkingSetBytes, process.WorkingSet64);
                samples++;
                if (Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds(4))
                    Assert.IsFalse((await peer.Cache.TryGetAsync<string>(keys[^1], local, token)).HasValue);
                await Task.Delay(500, token);
            }
            Assert.IsTrue(recovery.PendingCounts.Count >= 2, "长故障窗口内必须观察多次实际恢复尝试。");
            // SDK 的数量检查、淘汰和插入不是原子操作；反复并发可累计越过阈值，不能假设最多多出四项。
            // 本实验只约束恢复项来自已提交的 128 个唯一目标，实际峰值完整记录；严格全局内存预算仍待验收。
            TestContext.WriteLine("configuredRecoveryThreshold={0} pendingCounts={1} thresholdExceeded={2}",
                RecoveryLimit, string.Join(",", recovery.PendingCounts), recovery.PendingCounts.Any(count => count > RecoveryLimit));
            Assert.IsTrue(recovery.PendingCounts.All(count => count is > 0 && count <= keys.Length));
            foreach (var key in keys)
            {
                Assert.IsFalse((await peer.Cache.TryGetAsync<string>(key, local, token)).HasValue);
                Assert.IsFalse((await primary.Cache.TryGetAsync<string>(key, options, token)).HasValue,
                    "发布失败不能掩盖已完成的真实 L2 删除。");
            }
            await writer.SetPermissionsAsync("+publish");
            await reader.SetPermissionsAsync("on");
            var sentinel = Keys(writer.Suffix + "-rejoined", 1)[0];
            var received = 0;
            peer.Cache.Events.Backplane.MessageReceived += (_, args) =>
            {
                if (args.Message.SourceId == primary.Cache.InstanceId && args.Message.CacheKey?.EndsWith(sentinel, StringComparison.Ordinal) == true)
                    Interlocked.Increment(ref received);
            };
            var recoveredOptions = primary.Options(TimeSpan.FromMinutes(2));
            await WaitAsync(async () =>
            {
                await primary.Cache.SetAsync(sentinel, "rejoined", recoveredOptions, token: token);
                return Volatile.Read(ref received) > 0;
            }, token);
            Assert.AreEqual("rejoined", (await peer.Cache.TryGetAsync<string>(sentinel, recoveredOptions, token)).Value);
            var beforeRemove = Volatile.Read(ref received);
            await primary.Cache.RemoveAsync(sentinel, recoveredOptions, token);
            await WaitAsync(async () => Volatile.Read(ref received) > beforeRemove
                && !(await peer.Cache.TryGetAsync<string>(sentinel, local, token)).HasValue, token);
            TestContext.WriteLine("outageSeconds=30 targets=128 samples={0} recoveryAttempts={1} maxObservedPending={2} managedPeakBytes={3} processWorkingSetPeakBytes={4}",
                samples, recovery.PendingCounts.Count, recovery.PendingCounts.Max(), peakManagedBytes, peakWorkingSetBytes);
        }
        finally
        {
            await writer.SetPermissionsAsync("+publish");
            await reader.SetPermissionsAsync("on");
        }
    }

    [TestMethod]
    public async Task Disposal_with_actual_recovery_backlog_stops_retries_and_releases_owned_Redis_connections()
    {
        await using var writer = await RedisUser.CreateAsync();
        var recovery = new RecoveryLog();
        var primary = await Node.CreateAsync(writer, writer.Suffix, recovery: recovery);
        Task? disposal = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        try
        {
            var options = primary.Options(TimeSpan.FromMinutes(2));
            var keys = Keys(writer.Suffix, 128);
            foreach (var key in keys) await primary.Cache.SetAsync(key, "old", options, token: token);
            await writer.SetPermissionsAsync("-publish");
            await writer.AssertPublishDeniedAsync();
            var failed = await CaptureAsync(() => primary.Provider.GetRequiredService<ICacheBatchInvalidator>()
                .RemoveAsync(keys.Select(Target), token).AsTask());
            Assert.IsNotNull(failed);
            Assert.IsTrue(recovery.MaxSizeReached);
            await WaitAsync(() => Task.FromResult(!recovery.PendingCounts.IsEmpty), token);
            var connectionsBefore = await writer.ConnectionCountAsync();
            Assert.IsTrue(connectionsBefore > 0, "关闭前必须证明节点确实拥有该独占用户的 Redis 连接。");
            var pendingBefore = recovery.PendingCounts.Last();
            var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
            var elapsed = Stopwatch.StartNew();
            disposal = primary.DisposeAsync().AsTask();
            // 始终观察同一个关闭任务；不能用 WaitAsync 超时后遗留仍在执行的 Dispose。
            await disposal;
            elapsed.Stop();
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5), "本实验的 SDK 关闭应在五秒观察预算内完成。");
            await WaitAsync(async () => await writer.ConnectionCountAsync() == 0, token);
            var startsAfterDispose = recovery.PendingCounts.Count;
            // 两个实际恢复周期内不应再启动重试；这不是可靠排空或远端确认的承诺。
            await Task.Delay(TimeSpan.FromMilliseconds(4250), token);
            Assert.AreEqual(startsAfterDispose, recovery.PendingCounts.Count);
            Assert.AreEqual(0, await writer.ConnectionCountAsync());
            TestContext.WriteLine("shutdownMs={0:F2} pendingBeforeShutdown={1} recoveryStartsAfterDispose={2} ownedConnectionsBefore={3} ownedConnectionsAfter=0 managedBeforeBytes={4} managedAfterBytes={5}",
                elapsed.Elapsed.TotalMilliseconds, pendingBefore, recovery.PendingCounts.Count - startsAfterDispose,
                connectionsBefore, managedBefore, GC.GetTotalMemory(forceFullCollection: false));
        }
        finally
        {
            if (disposal is null) await primary.DisposeAsync();
            else await disposal;
            await writer.SetPermissionsAsync("+publish");
        }
    }

    private static string[] Keys(string suffix, int count) => Enumerable.Range(0, count)
        .Select(index => CacheKeyBuilder.ForGlobal("Testing", "fixture", "resources", $"{suffix}:{index}", "v1")).ToArray();

    private static CacheInvalidationTarget Target(string key) => new(EntryName, key,
        CacheInvalidationTargetKind.Key, CacheInvalidationScope.AllLayersSynchronous);

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; } catch (Exception exception) { return exception; }
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }

    /// <summary>只延迟真实失效调用；内层始终执行当前 Provider，不注入假 Redis 删除结果。</summary>
    private sealed class ObservedInvalidator(ICacheInvalidator inner, OperationGate gate) : ICacheInvalidator
    {
        public ValueTask RemoveAsync(string entryName, string key, CacheInvalidationScope scope, CancellationToken token = default) =>
            ExecuteAsync(() => inner.RemoveAsync(entryName, key, scope, token), token);

        public ValueTask RemoveByTagAsync(string entryName, string tag, CacheInvalidationScope scope, CancellationToken token = default) =>
            ExecuteAsync(() => inner.RemoveByTagAsync(entryName, tag, scope, token), token);

        private async ValueTask ExecuteAsync(Func<ValueTask> execute, CancellationToken token)
        {
            gate.Enter();
            try { await gate.Release.Task.WaitAsync(token); await execute(); }
            finally { gate.Exit(); }
        }
    }

    private sealed class OperationGate
    {
        private int _active, _peak, _calls;
        internal int Active => Volatile.Read(ref _active);
        internal int Peak => Volatile.Read(ref _peak);
        internal int Calls => Volatile.Read(ref _calls);
        internal TaskCompletionSource FourStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Enter()
        {
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = Volatile.Read(ref _peak); if (previous >= active) break; }
            while (Interlocked.CompareExchange(ref _peak, active, previous) != previous);
            if (active >= Concurrency) FourStarted.TrySetResult();
        }
        internal void Exit() => Interlocked.Decrement(ref _active);
    }

    /// <summary>节点只拥有自身 DI/监控生命周期；所有 Redis 操作限制在本实验命名空间与独占用户。</summary>
    private sealed class Node(ServiceProvider provider) : IAsyncDisposable
    {
        internal ServiceProvider Provider => provider;
        internal IFusionCache Cache => provider.GetRequiredService<IFusionCache>();
        internal static async Task<Node> CreateAsync(RedisUser user, string application,
            OperationGate? gate = null, RecoveryLog? recovery = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            if (recovery is not null) services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recovery));
            services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisConnectionString"] = user.Connection,
                ["Cache:ApplicationName"] = "resources-" + application,
                ["Cache:InvalidationMaxBatchItems"] = "128",
                ["Cache:InvalidationMaxConcurrentBatches"] = "2",
                ["Cache:InvalidationMaxConcurrency"] = "4",
                ["Cache:InvalidationTimeout"] = "00:00:30",
                ["Cache:AutoRecoveryMaxItems"] = "16",
                ["Cache:AutoRecoveryMaxRetryCount"] = "3",
                [$"Cache:Entries:{EntryName}:OwnerModule"] = "fixture",
                [$"Cache:Entries:{EntryName}:ConsistencyClass"] = "S1",
                [$"Cache:Entries:{EntryName}:L1Duration"] = "00:05:00",
                [$"Cache:Entries:{EntryName}:L2Duration"] = "00:05:00",
                [$"Cache:Entries:{EntryName}:Jitter"] = "00:00:00",
            }).Build(), "Testing");
            services.PostConfigure<FusionCacheOptions>(options =>
            {
                options.WaitForInitialBackplaneSubscribe = true;
                options.EnableAutoRecovery = recovery is not null;
                options.AutoRecoveryDelay = TimeSpan.FromSeconds(2);
                options.BackplaneCircuitBreakerDuration = TimeSpan.FromMilliseconds(200);
            });
            if (gate is not null)
            {
                var original = services.Last(descriptor => descriptor.ServiceType == typeof(ICacheInvalidator));
                services.RemoveAll<ICacheInvalidator>();
                services.AddSingleton<ICacheInvalidator>(sp => new ObservedInvalidator(
                    (ICacheInvalidator)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), gate));
            }
            var node = new Node(services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
            try
            {
                _ = node.Cache;
                foreach (var monitor in node.Provider.GetServices<IHostedService>()) await monitor.StartAsync(CancellationToken.None);
                return node;
            }
            catch { await node.DisposeAsync(); throw; }
        }
        internal FusionCacheEntryOptions Options(TimeSpan duration)
        {
            var options = Provider.GetRequiredService<ICachePolicyRegistry>().CreateEntryOptions(EntryName);
            options.Duration = duration;
            options.MemoryCacheDuration = duration;
            options.DistributedCacheDuration = duration;
            options.AllowBackgroundDistributedCacheOperations = false;
            options.AllowBackgroundBackplaneOperations = false;
            return options;
        }
        public async ValueTask DisposeAsync()
        {
            try { foreach (var monitor in Provider.GetServices<IHostedService>()) await monitor.StopAsync(CancellationToken.None); }
            finally { await Provider.DisposeAsync(); }
        }
    }

    private sealed class RedisUser(ConnectionMultiplexer admin, string suffix, string user, string connection) : IAsyncDisposable
    {
        internal string Suffix => suffix;
        internal string Connection => connection;
        internal static async Task<RedisUser> CreateAsync()
        {
            var configuration = ConfigurationOptions.Parse(await SharedDatabaseFixture.GetRedisConnectionStringAsync());
            configuration.AllowAdmin = true;
            var admin = await ConnectionMultiplexer.ConnectAsync(configuration);
            var suffix = Guid.NewGuid().ToString("N");
            var user = "fullnet-cache-resource-" + suffix;
            var password = Guid.NewGuid().ToString("N");
            try
            {
                await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", user, "reset", "on", ">" + password, "~*", "&*", "+@all");
                configuration.User = user;
                configuration.Password = password;
                return new RedisUser(admin, suffix, user, configuration.ToString(includePassword: true));
            }
            catch { await admin.DisposeAsync(); throw; }
        }
        internal async Task SetPermissionsAsync(params string[] rules) =>
            await admin.GetDatabase().ExecuteAsync("ACL", ["SETUSER", user, .. rules.Cast<object>()]);
        internal async Task DisconnectAsync()
        {
            await SetPermissionsAsync("off");
            Assert.IsTrue((long)await admin.GetDatabase().ExecuteAsync("CLIENT", "KILL", "USER", user) > 0);
        }
        internal async Task AssertPublishDeniedAsync()
        {
            using var client = await ConnectionMultiplexer.ConnectAsync(Connection);
            var failure = await CaptureAsync(async () => await client.GetSubscriber().PublishAsync(RedisChannel.Literal("fixture:" + Suffix), "control"));
            Assert.IsNotNull(failure);
            Assert.Contains("NOPERM", failure.ToString());
        }
        internal async Task<int> ConnectionCountAsync()
        {
            // 仅统计本实验独占用户；不读取或关闭其他任务的 Redis 连接。
            var clients = (await admin.GetDatabase().ExecuteAsync("CLIENT", "LIST")).ToString();
            return clients.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("user=" + user, StringComparer.Ordinal));
        }
        public async ValueTask DisposeAsync()
        {
            try { await admin.GetDatabase().ExecuteAsync("ACL", "DELUSER", user); }
            finally { await admin.DisposeAsync(); }
        }
    }

    /// <summary>仅保留 SDK 的恢复数量与容量分支，不存日志原文中的键和凭据。</summary>
    private sealed class RecoveryLog : ILoggerProvider, ILogger
    {
        private int _maxSizeReached;
        internal bool MaxSizeReached => Volatile.Read(ref _maxSizeReached) != 0;
        internal ConcurrentQueue<int> PendingCounts { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var values = fields.ToDictionary(item => item.Key, item => item.Value);
            var template = values.GetValueOrDefault("{OriginalFormat}") as string;
            if (template?.Contains("queue has reached the max size", StringComparison.Ordinal) == true)
                Interlocked.Exchange(ref _maxSizeReached, 1);
            if (template?.Contains("starting auto-recovery of", StringComparison.Ordinal) == true && values.GetValueOrDefault("Count") is int count)
                PendingCounts.Enqueue(count);
        }
    }
}
