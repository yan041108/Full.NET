using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;
using Full.NET.Modules.Settings.Features.ManageHostConfigEntries;
using Full.NET.Modules.Settings.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Settings;

/// <summary>单元测试（设置）：Diagnostic Policy Store。</summary>
[TestClass]
public sealed class DiagnosticPolicyStoreTests
{
    [TestMethod]
    public async Task Snapshot_expiry_revokes_rules_even_when_background_poller_stops()
    {
        var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        var (store, query, _) = CreateStore(clock);
        query.Row = ActivePolicyRow();
        await store.RefreshAsync(0, CancellationToken.None);
        Assert.IsFalse(store.Current.IsDefault);
        var count = query.QueryCount;

        clock.UtcNow.Returns(now.AddMinutes(2));
        Assert.IsTrue(store.Current.IsDefault);
        Assert.IsTrue((await store.GetCurrentAsync(CancellationToken.None)).IsDefault);
        Assert.AreEqual(count, query.QueryCount);
    }

    [TestMethod]
    public async Task Authority_failure_replaces_widening_snapshot_with_safe_default()
    {
        var (store, query, _) = CreateStore();
        query.Row = ActivePolicyRow();
        await store.RefreshAsync(0, CancellationToken.None);
        Assert.IsFalse(store.Current.IsDefault);

        query.Failure = new InvalidOperationException("authority unavailable");
        await store.RefreshAsync(0, CancellationToken.None);

        Assert.IsTrue(store.Current.IsDefault);
        Assert.AreEqual(0, store.Current.Version);
        Assert.AreEqual(0, store.Current.ActiveRules.Count);
    }

    [TestMethod]
    public async Task Missing_authoritative_policy_restores_default_after_cache_failure()
    {
        var (store, query, _) = CreateStore();
        query.Row = ActivePolicyRow();
        await store.RefreshAsync(0, CancellationToken.None);
        Assert.IsFalse(store.Current.IsDefault);

        query.Row = null;
        await store.RefreshAsync(0, CancellationToken.None);

        Assert.IsTrue(store.Current.IsDefault);
        Assert.AreEqual(0, store.Current.Version);
    }

    [TestMethod]
    public async Task Authoritative_policy_can_replace_older_generation_after_missed_restore()
    {
        var (store, query, _) = CreateStore();
        query.Row = ActivePolicyRow(documentVersion: 4);
        await store.RefreshAsync(0, CancellationToken.None);
        Assert.AreEqual(4, store.Current.Version);

        // 另一节点在两次轮询间恢复默认，再创建了新一代策略文档。
        query.Row = ActivePolicyRow(documentVersion: 1);
        await store.RefreshAsync(0, CancellationToken.None);

        Assert.AreEqual(1, store.Current.Version);
    }

    [TestMethod]
    public async Task Background_refresh_observes_remote_restore_without_request_path_reload()
    {
        var (store, query, policies) = CreateStore();
        query.Row = ActivePolicyRow();
        using var cancellation = new CancellationTokenSource();
        var service = new DiagnosticPolicyRefreshService(store);
        var running = service.RunAsync(TimeSpan.FromMilliseconds(20), cancellation.Token);
        try
        {
            await WaitUntilAsync(() => store.Current.Version == 4);
            query.Row = null;
            await WaitUntilAsync(() => store.Current.IsDefault);
            policies.DidNotReceive().GetRequired(Arg.Any<string>());
        }
        finally
        {
            await cancellation.CancelAsync();
            await running;
        }
    }

    [TestMethod]
    public async Task Background_poll_failure_revokes_widening_rule()
    {
        var (store, query, policies) = CreateStore();
        query.Row = ActivePolicyRow();
        await store.PollAuthorityAsync(CancellationToken.None);
        Assert.AreEqual(4, store.Current.Version);

        query.Failure = new InvalidOperationException("authority unavailable");
        await store.PollAuthorityAsync(CancellationToken.None);

        Assert.IsTrue(store.Current.IsDefault);
        policies.DidNotReceive().GetRequired(Arg.Any<string>());
    }

    [TestMethod]
    public async Task Management_read_uses_in_memory_snapshot_without_authority_query()
    {
        var (store, query, policies) = CreateStore();
        query.Row = ActivePolicyRow();

        var snapshot = await store.GetCurrentAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.IsDefault);
        Assert.AreEqual(0, query.QueryCount);
        policies.DidNotReceive().GetRequired(Arg.Any<string>());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static (DiagnosticPolicyStore Store, StubQueryExecutor Query, ICachePolicyRegistry Policies) CreateStore(IClock? suppliedClock = null)
    {
        var query = new StubQueryExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IQueryExecutor>(query);
        services.AddScoped(_ => Substitute.For<ICurrentTenantContextWriter>());
        var provider = services.BuildServiceProvider();
        var policies = Substitute.For<ICachePolicyRegistry>();
        policies.GetRequired(Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("cache unavailable"));
        var clock = suppliedClock ?? Substitute.For<IClock>();
        if (suppliedClock is null) clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return (new DiagnosticPolicyStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<DiagnosticPolicyStore>.Instance,
            Microsoft.Extensions.Options.Options.Create(new CacheOptions())), query, policies);
    }

    private static ConfigEntryRecord ActivePolicyRow(long documentVersion = 4)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new DiagnosticPolicyDocument(
            documentVersion,
            LoggingPressureState.Normal,
            [new DiagnosticPolicyRule(
                DiagnosticPolicyScopeKind.Category,
                LogClassification.HttpOperation,
                1,
                null,
                null,
                null,
                now.AddMinutes(10))]);
        return new ConfigEntryRecord(
            Guid.NewGuid(),
            DiagnosticPolicyLimits.ConfigKey,
            "diagnostic policy",
            null,
            null,
            "Json",
            JsonSerializer.Serialize(document, SettingsJsonSerializerContext.Default.DiagnosticPolicyDocument),
            0,
            true,
            now,
            null,
            1);
    }

    private sealed class StubQueryExecutor : IQueryExecutor
    {
        public ConfigEntryRecord? Row { get; set; }
        public Exception? Failure { get; set; }
        public int QueryCount { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            if (Failure is not null)
            {
                return Task.FromException<T?>(Failure);
            }

            if (typeof(T) != typeof(ConfigEntryRecord))
            {
                throw new InvalidOperationException($"Unexpected query {statement.Name}.");
            }

            return Task.FromResult((T?)(object?)Row);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
