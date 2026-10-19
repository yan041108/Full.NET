using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Observability;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.ObservabilityAdmin.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.IntegrationTests.Settings;

/// <summary>双宿主共享真实数据库和 Redis，验证诊断热路径使用的快照与缓存删除、管理刷新及权威轮询的边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DiagnosticPolicySnapshotTests
{
    [TestMethod]
    public async Task SqlServer_snapshot_refresh_is_node_local_and_authority_poll_converges()
    {
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_snapshot_refresh_is_node_local_and_authority_poll_converges()
    {
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var settings = new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = redis,
            ["Cache:ApplicationName"] = $"diagnostic-snapshot-{Guid.NewGuid():N}",
            ["ConnectionStrings:redis"] = redis,
        };
        var secondaryClock = new SnapshotClock();
        using var primary = new FullNetApiFactory(provider, connection, settings,
            configureTestServices: services => RemoveAutomaticPoll(services));
        using var secondary = new FullNetApiFactory(provider, connection, settings,
            configureTestServices: services =>
            {
                RemoveAutomaticPoll(services);
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(secondaryClock);
            });
        using var experiment = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = experiment.Token;
        await primary.InitializeAsync(token);
        await secondary.InitializeAsync(token);
        using var writer = primary.CreateClientForHost("localhost");
        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await primary.CreateHostAccessTokenAsync(
                [DiagnosticPolicyManagementPermissions.Read, DiagnosticPolicyManagementPermissions.Update], token));
        using var manager = secondary.CreateClientForHost("localhost");
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await secondary.CreateHostAccessTokenAsync([ObservabilityCachePolicyPermissions.Invalidate], token));
        var primaryStore = primary.Services.GetRequiredService<IDiagnosticPolicyStore>();
        var secondaryStore = secondary.Services.GetRequiredService<IDiagnosticPolicyStore>();
        Assert.AreNotSame(primaryStore, secondaryStore);
        var primaryCache = primary.Services.GetRequiredService<IFusionCache>();
        var secondaryCache = secondary.Services.GetRequiredService<IFusionCache>();
        Assert.AreNotEqual(primaryCache.InstanceId, secondaryCache.InstanceId);

        using var read = await writer.GetAsync("/api/v1/settings/diagnostic-policy", token);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode, await read.Content.ReadAsStringAsync(token));
        var initial = await read.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(token);
        Assert.IsNotNull(initial);
        var first = await UpdateAsync(writer, initial.ConfigEntryVersion, "Normal", "first", token);
        AssertSnapshot(primaryStore.Current, first, "first");

        // 故意向旧 L2 写入与权威版本不同的文档；第二个冷缓存节点必须先证明能从真实共享层读到它。
        var legacyKey = CacheKeyBuilder.ForGlobal("Testing", "settings", "diagnostic-policy", "current", "v1");
        var legacy = new DiagnosticPolicyDocument(999, LoggingPressureState.Critical, []);
        var legacyOptions = new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            AllowBackgroundDistributedCacheOperations = false,
            AllowBackgroundBackplaneOperations = false,
            ReThrowDistributedCacheExceptions = true,
            ReThrowBackplaneExceptions = true,
        };
        await primaryCache.SetAsync(legacyKey, legacy, legacyOptions, token: token);
        var fromRedis = await secondaryCache.TryGetAsync<DiagnosticPolicyDocument>(legacyKey, legacyOptions, token);
        Assert.IsTrue(fromRedis.HasValue, "阳性对照必须实际读到共享 Redis 文档。");
        Assert.AreEqual(999L, fromRedis.Value.Version);
        await secondaryStore.RefreshAsync(first.Version, token);
        AssertSnapshot(secondaryStore.Current, first, "first");

        var second = await UpdateAsync(writer, first.ConfigEntryVersion, "Degraded", "second", token);
        Assert.AreEqual(first.Version + 1, second.Version);
        AssertSnapshot(primaryStore.Current, second, "second");
        AssertSnapshot(secondaryStore.Current, first, "first");
        await primaryCache.SetAsync(legacyKey, legacy, legacyOptions, token: token);
        var l2Only = legacyOptions.Duplicate();
        l2Only.SkipMemoryCacheRead = true;
        l2Only.SkipMemoryCacheWrite = true;
        Assert.IsTrue((await primaryCache.TryGetAsync<DiagnosticPolicyDocument>(legacyKey, l2Only, token)).HasValue);
        await primary.Services.GetRequiredService<ICacheInvalidator>().RemoveAsync(
            CacheEntryNames.DiagnosticPolicy, legacyKey, CacheInvalidationScope.AllLayersSynchronous, token);
        Assert.IsFalse((await primaryCache.TryGetAsync<DiagnosticPolicyDocument>(legacyKey, l2Only, token)).HasValue,
            "必须真实删除 L2，不能用本机 L1 缺失冒充共享层删除。");
        // 即使共享文档已删除、Backplane 已发布，热路径快照仍由各节点独立持有。
        AssertSnapshot(primaryStore.Current, second, "second");
        AssertSnapshot(secondaryStore.Current, first, "first");

        using var invalidate = await manager.PostAsJsonAsync(
            $"/api/v1/observability/cache-policies/{CacheEntryNames.DiagnosticPolicy}/invalidations",
            new CacheInvalidationRequest("global", null, CacheInvalidationScopeNames.CurrentNodeOnly), token);
        Assert.AreEqual(HttpStatusCode.OK, invalidate.StatusCode, await invalidate.Content.ReadAsStringAsync(token));
        var invalidated = await invalidate.Content.ReadFromJsonAsync<CacheInvalidationResult>(token);
        Assert.IsNotNull(invalidated);
        CollectionAssert.Contains(invalidated.InvalidatedTargets.ToArray(),
            $"snapshot:{CacheEntryNames.DiagnosticPolicy}:current-node");
        AssertSnapshot(secondaryStore.Current, second, "second");

        DiagnosticPolicyResponse third;
        // 先观察首次加载完成，再提交下一版本；只有后续定时 tick 才能收敛，首次加载不能冒充轮询。
        var initialPollTime = secondaryStore.Current.LoadedAtUtc.AddSeconds(1);
        secondaryClock.FreezeAt(initialPollTime);
        var worker = new DiagnosticPolicyRefreshService(secondary.Services.GetRequiredService<DiagnosticPolicyStore>());
        using (var polling = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            polling.CancelAfter(TimeSpan.FromSeconds(10));
            var running = worker.RunAsync(TimeSpan.FromMilliseconds(100), polling.Token);
            try
            {
                while (secondaryStore.Current.LoadedAtUtc != initialPollTime)
                    await Task.Delay(20, polling.Token);
                polling.Token.ThrowIfCancellationRequested();
                AssertSnapshot(secondaryStore.Current, second, "second");
                third = await UpdateAsync(writer, second.ConfigEntryVersion, "Critical", "third", polling.Token);
                Assert.AreEqual(second.Version + 1, third.Version);
                AssertSnapshot(primaryStore.Current, third, "third");
                while (secondaryStore.Current.Version != third.Version)
                    await Task.Delay(20, polling.Token);
                polling.Token.ThrowIfCancellationRequested();
                AssertSnapshot(secondaryStore.Current, third, "third");
            }
            finally
            {
                await polling.CancelAsync();
                await running;
            }
        }

        // 暂停轮询后精确推进至最大快照年龄；规则仍未到期，回退不能被规则 TTL 混淆。
        var loaded = secondaryStore.Current;
        var maxAge = secondary.Services.GetRequiredService<IOptions<CacheOptions>>().Value.DiagnosticSnapshotMaxAge;
        Assert.IsTrue(loaded.ActiveRules[0].ExpiresAtUtc > loaded.LoadedAtUtc + maxAge);
        secondaryClock.FreezeAt(loaded.LoadedAtUtc + maxAge);
        var fallback = await secondaryStore.GetCurrentAsync(token);
        Assert.IsTrue(fallback.IsDefault);
        Assert.AreEqual(0L, fallback.Version);
        Assert.AreEqual(LoggingPressureState.Normal, fallback.PressureState);
        Assert.HasCount(0, fallback.ActiveRules);
        AssertSnapshot(primaryStore.Current, third, "third");
    }

    private static async Task<DiagnosticPolicyResponse> UpdateAsync(
        HttpClient client, int configVersion, string pressure, string scopeValue, CancellationToken token)
    {
        using var response = await client.PutAsJsonAsync("/api/v1/settings/diagnostic-policy",
            new UpdateDiagnosticPolicyRequest(pressure,
                [new DiagnosticPolicyRuleRequest("Endpoint", scopeValue, 1.0, null, null, null,
                    DateTimeOffset.UtcNow.AddMinutes(30))], configVersion), token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token));
        var updated = await response.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(token);
        Assert.IsNotNull(updated);
        return updated;
    }

    private static void AssertSnapshot(DiagnosticPolicySnapshot actual, DiagnosticPolicyResponse expected, string scopeValue)
    {
        Assert.IsFalse(actual.IsDefault);
        Assert.AreEqual(expected.Version, actual.Version);
        Assert.AreEqual(expected.PressureState, actual.PressureState.ToString());
        Assert.HasCount(1, actual.ActiveRules);
        Assert.AreEqual(scopeValue, actual.ActiveRules[0].ScopeValue);
        Assert.AreEqual(1.0, actual.ActiveRules[0].SuccessSampleRateOverride);
    }

    private static void RemoveAutomaticPoll(IServiceCollection services)
    {
        // 仅移除该服务的自动调度，保持所有业务注册；否则 30 秒背景刷新会让本地性反例偶然通过。
        var registration = services.Single(descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(DiagnosticPolicyRefreshService));
        services.Remove(registration);
    }

    private sealed class SnapshotClock : IClock
    {
        private long _frozenTicks;
        public DateTimeOffset UtcNow
        {
            get
            {
                var ticks = Interlocked.Read(ref _frozenTicks);
                return ticks != 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : DateTimeOffset.UtcNow;
            }
        }
        public void FreezeAt(DateTimeOffset now) => Interlocked.Exchange(ref _frozenTicks, now.UtcTicks);
    }
}
