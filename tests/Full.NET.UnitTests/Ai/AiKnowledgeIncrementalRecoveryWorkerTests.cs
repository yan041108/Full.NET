using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>补偿只消费服务端来源锚点；范围、事务和游标不能转化为收费执行授权。</summary>
[TestClass]
public sealed class AiKnowledgeIncrementalRecoveryWorkerTests
{
    [TestMethod]
    public void Worker_registers_recovery_loop_but_api_does_not()
    {
        var worker = new ServiceCollection(); var api = new ServiceCollection(); var config = new ConfigurationBuilder().Build();
        new AiModule().AddBackgroundServices(worker, config); new AiModule().AddServices(api, config);
        Assert.IsTrue(worker.Any(x => x.ServiceType == typeof(AiKnowledgeIncrementalRecoveryCursor) && x.Lifetime == ServiceLifetime.Singleton));
        Assert.IsTrue(worker.Any(x => x.ServiceType == typeof(AiKnowledgeIncrementalRecoveryWorker) && x.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(worker.Any(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AiKnowledgeIncrementalRecoveryHostedProcessor)));
        Assert.IsFalse(api.Any(x => x.ServiceType == typeof(AiKnowledgeIncrementalRecoveryWorker) || x.ImplementationType == typeof(AiKnowledgeIncrementalRecoveryHostedProcessor)));
    }

    [TestMethod]
    [DataRow(0, 10000)] [DataRow(33, 10000)] [DataRow(4, 999)] [DataRow(4, 60001)]
    public void Options_reject_unbounded_scans_even_when_disabled(int batch, int poll)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{AiKnowledgeIncrementalRecoveryWorkerOptions.SectionName}:BatchSize"] = batch.ToString(),
            [$"{AiKnowledgeIncrementalRecoveryWorkerOptions.SectionName}:PollMilliseconds"] = poll.ToString()
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config); new AiModule().AddBackgroundServices(services, config);
        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() => { _ = provider.GetRequiredService<IOptions<AiKnowledgeIncrementalRecoveryWorkerOptions>>().Value; });
    }

    [TestMethod]
    public async Task Default_disabled_worker_never_scans_or_creates_scopes()
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); var query = new Query(); var scopes = Substitute.For<IServiceScopeFactory>();
        Assert.IsFalse(new AiKnowledgeIncrementalRecoveryWorkerOptions().Enabled);
        Assert.AreEqual(0, await Worker(query, cursor, scopes, enabled: false).ProcessPendingAsync(default));
        using var hosted = new AiKnowledgeIncrementalRecoveryHostedProcessor(scopes, Options.Create(new AiKnowledgeIncrementalRecoveryWorkerOptions()), NullLogger<AiKnowledgeIncrementalRecoveryHostedProcessor>.Instance);
        await hosted.StartAsync(default); await hosted.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); await hosted.StopAsync(default);
        Assert.AreEqual(0, query.Calls); Assert.IsFalse(scopes.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(0, false)] [DataRow(0, true)] [DataRow(1, false)] [DataRow(1, true)] [DataRow(2, false)] [DataRow(2, true)]
    public async Task Scan_rechecks_cancellation_ambient_transaction_and_parent_host(int kind, bool after)
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); using var stop = new CancellationTokenSource();
        var state = Substitute.For<IDataTransactionState>(); var tenant = Host(); var query = new Query();
        void Change() { if (kind == 0) stop.Cancel(); else if (kind == 1) state.HasTransaction.Returns(true); else tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "Changed")); }
        if (after) query.Read = (_, _) => { Change(); return []; }; else Change();
        var worker = Worker(query, cursor, state: state, tenant: tenant);
        if (kind == 0) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => worker.ProcessPendingAsync(stop.Token));
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => worker.ProcessPendingAsync(stop.Token));
        Assert.AreEqual(after ? 1 : 0, query.Calls); Assert.IsNull(cursor.Id); Assert.AreEqual(1, cursor.Gate.CurrentCount);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public async Task Invalid_or_duplicate_scan_rows_are_rejected_before_scope_creation(int kind)
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); var row = Source(); var scopes = Substitute.For<IServiceScopeFactory>();
        var rows = kind switch
        {
            0 => new[] { row, Source() }, 1 => new[] { row with { DocumentId = Guid.Empty } },
            2 => new[] { row with { KnowledgeBaseId = Guid.Empty } }, 3 => new[] { row with { VersionId = Guid.Empty } },
            4 => new[] { row with { TenantId = Guid.Empty } }, _ => new[] { row, row }
        };
        var query = new Query { Read = (_, _) => rows };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Worker(query, cursor, scopes, batch: kind == 5 ? 2 : 1).ProcessPendingAsync(default));
        Assert.IsFalse(scopes.ReceivedCalls().Any()); Assert.IsNull(cursor.Id);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Failed_item_advances_and_empty_page_wraps_using_database_cursor(bool mysql)
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); var source = Source(); var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw new IOException("untrusted failure text"));
        var query = new Query { Read = (sql, p) =>
        {
            Assert.AreSame(mysql ? AiKnowledgeIncrementalRecoveryWorkerSql.ScanMySql : AiKnowledgeIncrementalRecoveryWorkerSql.ScanSqlServer, sql);
            Assert.AreEqual(0, p["HasCursor"]); Assert.AreEqual(1, p["BatchSize"]); return [source];
        } };
        var worker = Worker(query, cursor, scopes, mysql: mysql);
        Assert.AreEqual(1, await worker.ProcessPendingAsync(default)); Assert.AreEqual(source.DocumentId, cursor.Id);
        query.Read = (_, p) => { Assert.AreEqual(1, p["HasCursor"]); Assert.AreEqual(source.DocumentId, p["AfterDocumentId"]); return []; };
        Assert.AreEqual(0, await worker.ProcessPendingAsync(default)); Assert.IsNull(cursor.Id);
        Assert.AreEqual(1, scopes.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Each_source_gets_fresh_host_tenant_host_scope_without_paid_services()
    {
        var rows = new[] { Source(), Source() with { TenantId = Guid.CreateVersion7() }, Source() };
        var seen = new List<Guid?>(); var scopedInstances = new List<CurrentTenantAccessor>(); var query = new Query { Read = (_, _) => rows };
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>(); services.AddScoped<ICurrentTenant>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton(Substitute.For<IDataTransactionState>());
        services.AddScoped(p =>
        {
            var current = p.GetRequiredService<CurrentTenantAccessor>(); scopedInstances.Add(current);
            var read = new Query { Lock = () => { seen.Add(current.Id); return 1; } };
            var command = Substitute.For<ICommandExecutor>(); command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            var tx = Substitute.For<ICommandTransaction>();
            tx.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(c => (c.Arg<Func<CancellationToken, Task<bool>>>() ?? throw new InvalidOperationException())(c.Arg<CancellationToken>()));
            return new AiKnowledgeIncrementalIndexWriter(read, command, tx, current, Options.Create(new DatabaseOptions()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        });
        using var provider = services.BuildServiceProvider(); using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); var parent = Host();
        Assert.AreEqual(3, await Worker(query, cursor, provider.GetRequiredService<IServiceScopeFactory>(), batch: 4, tenant: parent).ProcessPendingAsync(default));
        CollectionAssert.AreEqual(rows.Select(r => r.TenantId).ToArray(), seen.ToArray()); Assert.AreEqual(3, scopedInstances.Distinct().Count());
        Assert.IsTrue(parent.IsHost); Assert.IsNull(parent.Id); Assert.IsNull(cursor.Id);
    }

    [TestMethod]
    public async Task Gate_serializes_scans_and_wait_cancellation_does_not_release_another_holder()
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); var query = new Query(); using var stop = new CancellationTokenSource();
        await cursor.Gate.WaitAsync(); var pending = Worker(query, cursor).ProcessPendingAsync(stop.Token);
        Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0, query.Calls); await stop.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending); Assert.AreEqual(0, cursor.Gate.CurrentCount);
        cursor.Gate.Release(); Assert.AreEqual(0, await Worker(query, cursor).ProcessPendingAsync(default)); Assert.AreEqual(1, cursor.Gate.CurrentCount);
    }

    [TestMethod]
    public async Task Shutdown_during_item_is_not_swallowed_and_keeps_the_cursor()
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); using var stop = new CancellationTokenSource(); var row = Source();
        var query = new Query { Read = (_, _) => [row] }; var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => { stop.Cancel(); throw new OperationCanceledException(stop.Token); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Worker(query, cursor, scopes).ProcessPendingAsync(stop.Token));
        Assert.IsNull(cursor.Id); Assert.AreEqual(1, cursor.Gate.CurrentCount);
    }

    [TestMethod]
    public async Task Enabled_hosted_loop_sets_host_before_resolving_worker_and_stops_during_delay()
    {
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor(); var query = new Query(); var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>(); services.AddScoped<ICurrentTenantContextWriter>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped(p => Worker(query, cursor, tenant: p.GetRequiredService<CurrentTenantAccessor>()));
        using var provider = services.BuildServiceProvider();
        using var hosted = new AiKnowledgeIncrementalRecoveryHostedProcessor(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiKnowledgeIncrementalRecoveryWorkerOptions { Enabled = true }), NullLogger<AiKnowledgeIncrementalRecoveryHostedProcessor>.Instance);
        await hosted.StartAsync(default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (query.Calls == 0) await Task.Delay(10, deadline.Token);
        await hosted.StopAsync(deadline.Token); await hosted.ExecuteTask!.WaitAsync(deadline.Token);
        Assert.AreEqual(1, query.Calls);
    }

    private static CurrentTenantAccessor Host() { var tenant = new CurrentTenantAccessor(); tenant.SetHost(); return tenant; }
    private static AiKnowledgeSourceConfirmed Source() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null);
    private static AiKnowledgeIncrementalRecoveryWorker Worker(Query query, AiKnowledgeIncrementalRecoveryCursor cursor, IServiceScopeFactory? scopes = null,
        bool enabled = true, int batch = 1, bool mysql = false, IDataTransactionState? state = null, CurrentTenantAccessor? tenant = null) =>
        new(query, state ?? Substitute.For<IDataTransactionState>(), tenant ?? Host(), scopes ?? Substitute.For<IServiceScopeFactory>(),
            Options.Create(new DatabaseOptions { Provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer }),
            Options.Create(new AiKnowledgeIncrementalRecoveryWorkerOptions { Enabled = enabled, BatchSize = batch }), cursor,
            NullLogger<AiKnowledgeIncrementalRecoveryWorker>.Instance);

    private sealed class Query : IQueryExecutor
    {
        internal int Calls { get; private set; }
        internal Func<SqlStatement, IReadOnlyDictionary<string, object?>, AiKnowledgeSourceConfirmed[]> Read { get; set; } = (_, _) => [];
        internal Func<int> Lock { get; init; } = () => 1;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult((IReadOnlyList<T>)(object)Read(statement, (IReadOnlyDictionary<string, object?>)parameters!)); }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => Task.FromResult((T?)(object)Lock());
    }
}
