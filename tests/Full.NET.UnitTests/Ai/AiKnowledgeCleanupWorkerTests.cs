using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>后台清理必须在没有 API 注册补齐的 Worker 中独立装配。</summary>
[TestClass]
public sealed class AiKnowledgeCleanupWorkerTests
{
    [TestMethod]
    public void Worker_registers_scoped_durable_cleanup_dependencies()
    {
        var services = new ServiceCollection();
        new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
        foreach (var type in new[] { typeof(AiKnowledgeIndexCleanupStore), typeof(AiKnowledgeImportStore), typeof(AiKnowledgeImportCoordinator) })
            Assert.IsTrue(services.Any(x => x.ServiceType == type && x.Lifetime == ServiceLifetime.Scoped), type.Name);
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(AiKnowledgeCleanupCursor) && x.Lifetime == ServiceLifetime.Singleton));
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AiKnowledgeCleanupHostedProcessor)));
    }

    [TestMethod]
    public void Api_never_registers_cleanup_loop()
    {
        var services = new ServiceCollection(); new AiModule().AddServices(services, new ConfigurationBuilder().Build());
        Assert.IsFalse(services.Any(x => x.ImplementationType == typeof(AiKnowledgeCleanupHostedProcessor)));
        Assert.IsFalse(services.Any(x => x.ServiceType == typeof(AiKnowledgeCleanupWorkerCoordinator)));
    }

    [TestMethod] [DataRow(0, 1000)] [DataRow(33, 1000)] [DataRow(4, 249)] [DataRow(4, 60001)]
    public void Configuration_rejects_unbounded_scans(int batch, int poll)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:BatchSize"] = batch.ToString(), [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:PollMilliseconds"] = poll.ToString() }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config); new AiModule().AddBackgroundServices(services, config);
        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() => { _ = provider.GetRequiredService<IOptions<AiKnowledgeCleanupWorkerOptions>>().Value; });
    }

    [TestMethod]
    public async Task Disabled_worker_has_no_query_or_child_scope()
    {
        using var cursor = new AiKnowledgeCleanupCursor(); var query = new Query();
        Assert.AreEqual(0, await Coordinator(query, cursor, enabled: false).ProcessPendingAsync(default)); Assert.AreEqual(0, query.Calls);
        var scopes = Substitute.For<IServiceScopeFactory>();
        using var hosted = new AiKnowledgeCleanupHostedProcessor(scopes, Options.Create(new AiKnowledgeCleanupWorkerOptions()), NullLogger<AiKnowledgeCleanupHostedProcessor>.Instance);
        await hosted.StartAsync(default); await hosted.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); await hosted.StopAsync(default);
        Assert.IsFalse(scopes.ReceivedCalls().Any());
    }

    [TestMethod] [DataRow(23)] [DataRow(8761)]
    public void Configuration_rejects_unsafe_retired_retention(int hours)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:RetiredGenerationRetentionHours"] = hours.ToString() }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config); new AiModule().AddBackgroundServices(services, config);
        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() => { _ = provider.GetRequiredService<IOptions<AiKnowledgeCleanupWorkerOptions>>().Value; });
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Retired_candidate_uses_fixed_retention_and_advances_after_failure(bool mysql)
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.CreateVersion7(); using var cursor = new AiKnowledgeCleanupCursor();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["RetiredGenerationCleanupEnabled"] = "true", ["RetiredGenerationRetentionHours"] = "24" }).Build();
        var options = new AiKnowledgeCleanupWorkerOptions { Enabled = true, BatchSize = 1 }; config.Bind(options);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var scopes = Substitute.For<IServiceScopeFactory>(); scopes.CreateScope().Returns(_ => throw new IOException("scope failure"));
        var query = new Query { Read = (_, p) =>
        {
            CollectionAssert.Contains(p.Keys.ToArray(), "RetiredBefore"); Assert.AreEqual(now.AddHours(-24), p["RetiredBefore"]);
            Assert.AreEqual(1, p["RetiredEnabled"]);
            return [new() { Id = id, Kind = 2, CreatedAtUtc = now, KnowledgeBaseId = id, DocumentId = id, OwnerUserId = id }];
        } };
        var worker = new AiKnowledgeCleanupWorkerCoordinator(query, Substitute.For<IDataTransactionState>(), scopes, clock,
            Options.Create(new DatabaseOptions { Provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer }), Options.Create(options), cursor,
            NullLogger<AiKnowledgeCleanupWorkerCoordinator>.Instance);
        Assert.AreEqual(1, await worker.ProcessPendingAsync(default)); Assert.AreEqual(id, cursor.Id);
        Assert.AreEqual(1, scopes.ReceivedCalls().Count());
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Cursor_advances_on_failed_item_and_wraps_across_scopes(bool mysql)
    {
        using var cursor = new AiKnowledgeCleanupCursor(); var id = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        var scopes = Substitute.For<IServiceScopeFactory>(); scopes.CreateScope().Returns(_ => throw new IOException("scope failure"));
        var query = new Query { Read = (sql, p) =>
        {
            Assert.AreSame(mysql ? AiKnowledgeCleanupWorkerSql.ScanMySql : AiKnowledgeCleanupWorkerSql.ScanSqlServer, sql);
            Assert.AreEqual(1, p["BatchSize"]); Assert.AreEqual(0, p["HasCursor"]);
            return [new() { Id = id, CreatedAtUtc = now, Kind = 1, KnowledgeBaseId = id, DocumentId = id, OwnerUserId = id }];
        } };
        Assert.AreEqual(1, await Coordinator(query, cursor, mysql: mysql, scopes: scopes).ProcessPendingAsync(default));
        Assert.AreEqual(id, cursor.Id);
        query.Read = (_, p) =>
        {
            Assert.AreEqual(1, p["HasCursor"]); Assert.AreEqual(id, p["AfterId"]); Assert.AreEqual(now, p["AfterCreatedAtUtc"]); Assert.AreEqual(1, p["AfterKind"]);
            return [];
        };
        Assert.AreEqual(0, await Coordinator(query, cursor, mysql: mysql).ProcessPendingAsync(default)); Assert.IsNull(cursor.Id);
        Assert.AreEqual(1, scopes.ReceivedCalls().Count());
    }

    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Cancellation_and_transaction_are_rechecked_before_and_after_scan(bool ambient, bool after)
    {
        using var cursor = new AiKnowledgeCleanupCursor(); using var stop = new CancellationTokenSource();
        var state = Substitute.For<IDataTransactionState>(); var query = new Query();
        void Change() { if (ambient) state.HasTransaction.Returns(true); else stop.Cancel(); }
        if (after) query.Read = (_, _) => { Change(); return []; }; else Change();
        var worker = Coordinator(query, cursor, state: state);
        if (ambient) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => worker.ProcessPendingAsync(stop.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => worker.ProcessPendingAsync(stop.Token));
        Assert.AreEqual(after ? 1 : 0, query.Calls); Assert.IsNull(cursor.Id); Assert.AreEqual(1, cursor.Gate.CurrentCount);
    }

    [TestMethod]
    public async Task Oversized_scan_is_rejected_without_advancing_cursor()
    {
        using var cursor = new AiKnowledgeCleanupCursor(); var query = new Query { Read = (_, _) => [new(), new()] };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Coordinator(query, cursor).ProcessPendingAsync(default)); Assert.IsNull(cursor.Id);
    }

    [TestMethod]
    public async Task Hosted_shutdown_interrupts_poll_even_after_full_batch()
    {
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new Query { Read = (_, _) => { scanned.TrySetResult(); return [new() { Id = Guid.CreateVersion7() }]; } };
        using var cursor = new AiKnowledgeCleanupCursor();
        var options = Options.Create(new AiKnowledgeCleanupWorkerOptions { Enabled = true, BatchSize = 1, PollMilliseconds = 60000 });
        var services = new ServiceCollection(); services.AddScoped(_ => Coordinator(query, cursor));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var hosted = new AiKnowledgeCleanupHostedProcessor(provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<AiKnowledgeCleanupHostedProcessor>.Instance);
        await hosted.StartAsync(default); await scanned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await hosted.StopAsync(stop.Token);
        Assert.IsTrue(hosted.ExecuteTask!.IsCompletedSuccessfully); Assert.AreEqual(1, query.Calls);
    }

    private static AiKnowledgeCleanupWorkerCoordinator Coordinator(Query query, AiKnowledgeCleanupCursor cursor, bool enabled = true, bool mysql = false,
        IDataTransactionState? state = null, IServiceScopeFactory? scopes = null) => new(query, state ?? Substitute.For<IDataTransactionState>(), scopes ?? Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IClock>(), Options.Create(new DatabaseOptions { Provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer }),
            Options.Create(new AiKnowledgeCleanupWorkerOptions { Enabled = enabled, BatchSize = 1 }), cursor, NullLogger<AiKnowledgeCleanupWorkerCoordinator>.Instance);

    // 内部投影使用显式执行器替身，避免动态代理要求扩大生产可见性。
    private sealed class Query : IQueryExecutor
    {
        internal int Calls;
        internal Func<SqlStatement, Dictionary<string, object?>, IReadOnlyList<AiKnowledgeCleanupRecord>>? Read;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Calls++; Assert.AreEqual(typeof(AiKnowledgeCleanupRecord), typeof(T));
            return Task.FromResult((IReadOnlyList<T>)(object)(Read?.Invoke(statement, (Dictionary<string, object?>)parameters!) ?? []));
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
