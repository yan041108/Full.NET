using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageMyGridPreferences;
using Full.NET.Modules.Settings.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>真实双库、Grid 业务服务和 Redis：确定性制造提交后旧值回填，并验证无通知时的 TTL 收敛。</summary>
[TestClass]
[DoNotParallelize]
public sealed class GridRedisRefillRaceTests
{
    private const string Grid = "identity.users";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    [TestMethod]
    public Task SqlServer_committed_grid_delete_with_late_refill_converges_without_backplane() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_committed_grid_delete_with_late_refill_converges_without_backplane() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider databaseProvider)
    {
        var connection = databaseProvider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var gate = new ReadGate();
        var settings = new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = await SharedDatabaseFixture.GetRedisConnectionStringAsync(),
            ["Cache:ApplicationName"] = $"grid-refill-{Guid.NewGuid():N}",
            [$"Cache:Entries:{CacheEntryNames.GridPreference}:OwnerModule"] = "settings",
            [$"Cache:Entries:{CacheEntryNames.GridPreference}:ConsistencyClass"] = "S2",
            [$"Cache:Entries:{CacheEntryNames.GridPreference}:L1Duration"] = "00:00:08",
            [$"Cache:Entries:{CacheEntryNames.GridPreference}:L2Duration"] = "00:00:08",
            [$"Cache:Entries:{CacheEntryNames.GridPreference}:Jitter"] = "00:00:00",
            [$"Cache:Entries:{CacheEntryNames.GridPreference}:FailSafeEnabled"] = "false",
        };
        using var primary = new FullNetApiFactory(databaseProvider, connection, settings, configureTestServices: services =>
        {
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(IQueryExecutor));
            services.RemoveAll<IQueryExecutor>();
            services.Add(new ServiceDescriptor(typeof(IQueryExecutor), sp => new GatedQuery(
                (IQueryExecutor)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), gate), original.Lifetime));
        });
        using var secondary = primary.CreateIsolatedFactory();
        await primary.InitializeAsync();
        await secondary.InitializeAsync();
        var user = await primary.CreateHostIdentityAsync($"grid-refill-{Guid.CreateVersion7():N}", []);
        var caches = new[] { primary, secondary }.Select(factory => factory.Services.GetRequiredService<IFusionCache>()).ToArray();
        Assert.AreNotEqual(caches[0].InstanceId, caches[1].InstanceId);
        foreach (var cache in caches)
        {
            Assert.IsTrue(cache.HasDistributedCache);
            // 明确排除广播补偿；本实验只证明展示类业务结果在实际两层 TTL 后收敛。
            cache.RemoveBackplane();
        }
        foreach (var factory in new[] { primary, secondary })
        {
            var policy = factory.Services.GetRequiredService<ICachePolicyRegistry>().GetRequired(CacheEntryNames.GridPreference);
            Assert.AreEqual(Lifetime, policy.L1Duration);
            Assert.AreEqual(Lifetime, policy.L2Duration);
            Assert.AreEqual(TimeSpan.Zero, policy.Jitter);
            Assert.IsFalse(policy.FailSafeEnabled);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var seeded = await InScopeAsync(primary, service => service.PutAsync(user.UserId, Grid, new UpdateGridPreferenceRequest(2, [], 0), timeout.Token));
        Assert.IsTrue(seeded.IsSuccess);
        Assert.AreEqual(1, seeded.Value!.Version);
        gate.Arm();
        var pending = InScopeAsync(primary, service => service.GetAsync(user.UserId, Grid, timeout.Token));
        try
        {
            var actualOldRow = await gate.Started.Task.WaitAsync(timeout.Token);
            Assert.IsNotNull(actualOldRow, "gate 必须截获真实数据库物化行，不伪造旧快照。");
            Assert.AreEqual(1, actualOldRow.Version);
            var deleted = await InScopeAsync(secondary, service => service.DeleteAsync(user.UserId, Grid, timeout.Token));
            Assert.IsTrue(deleted.IsSuccess);
            // DeleteAsync 已返回，事务与提交后两层失效已经结束；另一作用域重查实际数据库必须为空。
            await using (var authority = secondary.Services.CreateAsyncScope())
            {
                var row = await authority.ServiceProvider.GetRequiredService<IQueryExecutor>()
                    .QuerySingleOrDefaultAsync<GridPreferenceRecord>(GridPreferenceSql.FindByUserAndGrid,
                        SettingsSqlParameters.Create(("UserId", user.UserId), ("GridKey", Grid)), timeout.Token);
                Assert.IsNull(row);
            }

            var releaseAt = Stopwatch.GetTimestamp();
            gate.Release.TrySetResult();
            Assert.AreEqual(1, (await pending).Value!.Version, "提交前已开始的展示请求允许返回其旧快照。");
            var key = CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference", $"{user.UserId:N}:{Grid}", "v2");
            var options = primary.Services.GetRequiredService<ICachePolicyRegistry>().CreateEntryOptions(CacheEntryNames.GridPreference);
            var local = options.Duplicate();
            local.SetSkipDistributedCache(skip: true, skipBackplaneNotifications: true);
            Assert.IsFalse((await caches[1].TryGetAsync<GridPreferenceResponse>(key, local, token: timeout.Token)).HasValue);
            var shared = options.Duplicate();
            shared.SkipMemoryCacheRead = true;
            shared.SkipMemoryCacheWrite = true;
            var sharedOld = await caches[1].TryGetAsync<GridPreferenceResponse>(key, shared, token: timeout.Token);
            Assert.IsTrue(sharedOld.HasValue);
            Assert.AreEqual(1, sharedOld.Value.Version,
                "冷节点必须从真实 Redis 读到迟到旧值，不能用本地命中代替 L2 观察。");
            var readsBeforeHits = Volatile.Read(ref gate.GridReads);
            foreach (var factory in new[] { primary, secondary })
                Assert.AreEqual(1, (await InScopeAsync(factory, service => service.GetAsync(user.UserId, Grid, timeout.Token))).Value!.Version);
            Assert.AreEqual(readsBeforeHits, Volatile.Read(ref gate.GridReads), "两个节点的旧展示结果必须来自缓存。");
            Assert.IsTrue(Stopwatch.GetElapsedTime(releaseAt) < Lifetime / 2, "预热过晚不能作为 TTL 收敛证据。");

            // 不主动删除/更新缓存；只等待 Redis 自然过期，两实例均须实际回源并恢复已提交的默认展示结果。
            foreach (var factory in new[] { primary, secondary })
            {
                while ((await InScopeAsync(factory, service => service.GetAsync(user.UserId, Grid, timeout.Token))).Value!.Version != 0)
                    await Task.Delay(50, timeout.Token);
            }
            Assert.IsTrue(Volatile.Read(ref gate.GridReads) > readsBeforeHits);
            foreach (var factory in new[] { primary, secondary })
                Assert.AreEqual(0, (await InScopeAsync(factory, service => service.GetAsync(user.UserId, Grid, timeout.Token))).Value!.Version);
        }
        finally
        {
            gate.Release.TrySetResult();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    private static async Task<Result<GridPreferenceResponse>> InScopeAsync(FullNetApiFactory factory,
        Func<MyGridPreferenceService, Task<Result<GridPreferenceResponse>>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try { return await action(scope.ServiceProvider.GetRequiredService<MyGridPreferenceService>()); }
        finally { tenant.Clear(); }
    }

    private sealed class ReadGate
    {
        private int _armed;
        internal int GridReads;
        internal TaskCompletionSource<GridPreferenceRecord?> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);
        internal bool Capture() => Interlocked.Exchange(ref _armed, 0) == 1;
    }

    private sealed class GatedQuery(IQueryExecutor inner, ReadGate gate) : IQueryExecutor
    {
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var row = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (typeof(T) == typeof(GridPreferenceRecord))
            {
                Interlocked.Increment(ref gate.GridReads);
                if (gate.Capture())
                {
                    gate.Started.TrySetResult((GridPreferenceRecord?)(object?)row);
                    await gate.Release.Task.WaitAsync(cancellationToken);
                }
            }
            return row;
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) => inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }
}
