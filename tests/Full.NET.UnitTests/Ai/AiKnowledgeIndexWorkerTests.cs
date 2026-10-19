using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>后台边界先于实际双库验证；不替换实际消费者或把静态 SQL 检查当成执行证据。</summary>
[TestClass]
public sealed class AiKnowledgeIndexWorkerTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Background_catalog_preserves_normal_users_exact_index_permissions(bool tenant)
    {
        var services = new ServiceCollection(); var module = new AiModule();
        module.AddBackgroundServices(services, new ConfigurationBuilder().Build());
        module.AddBackgroundServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var contributors = provider.GetServices<IAuthorizationCatalogContributor>().ToArray();
        var required = new[] { AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue };
        var query = new WorkerQuery { Grants = required.Append("ai.knowledge.unknown").Select(code => new IdentityAuthorizationRow(code, false)).ToArray() };
        var reader = new PermissionSnapshotReader(query, AuthorizationCatalog.Create(contributors));
        var snapshot = await reader.ReadAsync(Guid.CreateVersion7(), tenant ? "tenant" : "host", tenant ? Guid.CreateVersion7() : null, default);
        CollectionAssert.AreEquivalent(required, snapshot.Permissions.ToArray()); Assert.IsFalse(snapshot.IsSuperAdministrator);
        Assert.AreEqual(1, contributors.Count(c => c is AiAuthorizationContributor));
    }

    [TestMethod]
    public void Background_registration_has_scoped_execution_and_singleton_loop()
    {
        var services = new ServiceCollection(); new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
        foreach (var type in new[] { typeof(AiBackgroundLogCorrelation), typeof(AiKnowledgeIndexWorkerCoordinator), typeof(AiKnowledgeIndexTaskStore), typeof(AiKnowledgeIndexSessionService),
            typeof(AiKnowledgeIndexBatchStore), typeof(AiKnowledgeIndexBudgetService), typeof(AiKnowledgeIndexEmbeddingService), typeof(AiKnowledgeIndexVectorStore),
            typeof(AiKnowledgeIndexTextManifestStore), typeof(AiKnowledgeIndexActivationStore), typeof(AiKnowledgeIndexPersistenceService) })
            Assert.IsTrue(services.Any(d => d.ServiceType == type && d.Lifetime == ServiceLifetime.Scoped), type.Name);
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AiKnowledgeIndexHostedProcessor) && d.Lifetime == ServiceLifetime.Singleton));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        Assert.AreSame(first.ServiceProvider.GetRequiredService<AiBackgroundLogCorrelation>(), first.ServiceProvider.GetRequiredService<AiBackgroundLogCorrelation>());
        Assert.AreNotSame(first.ServiceProvider.GetRequiredService<AiBackgroundLogCorrelation>(), second.ServiceProvider.GetRequiredService<AiBackgroundLogCorrelation>());
    }

    [TestMethod]
    public void Api_registration_does_not_start_index_loop()
    {
        var services = new ServiceCollection(); new AiModule().AddServices(services, new ConfigurationBuilder().Build());
        Assert.IsFalse(services.Any(d => d.ImplementationType == typeof(AiKnowledgeIndexHostedProcessor)));
        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(AiKnowledgeIndexWorkerCoordinator)));
    }

    [TestMethod] [DataRow(0, 1000)] [DataRow(33, 1000)] [DataRow(4, 249)] [DataRow(4, 60001)]
    public void Worker_configuration_rejects_unbounded_polling(int batch, int poll)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [$"{AiKnowledgeIndexWorkerOptions.SectionName}:BatchSize"] = batch.ToString(), [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"] = poll.ToString() }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config); new AiModule().AddBackgroundServices(services, config);
        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() => { _ = provider.GetRequiredService<IOptions<AiKnowledgeIndexWorkerOptions>>().Value; });
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Enabled_scan_uses_bounded_provider_sql(bool mysql)
    {
        var query = new WorkerQuery(); SqlStatement? seen = null;
        query.Scan = (statement, parameters) =>
        {
            seen = statement; var p = (Dictionary<string, object?>)parameters!;
            Assert.AreEqual(4, p["BatchSize"]); Assert.IsInstanceOfType<DateTimeOffset>(p["Now"]); return Array.Empty<AiKnowledgeIndexWorkerRecord>();
        };
        Assert.AreEqual(0, await Coordinator(query, mysql, true).ProcessPendingAsync(default));
        Assert.AreSame(mysql ? AiKnowledgeIndexWorkerSql.ScanMySql : AiKnowledgeIndexWorkerSql.ScanSqlServer, seen);
    }

    [TestMethod]
    public async Task Disabled_worker_never_queries_or_creates_scope()
    {
        var query = new WorkerQuery(); Assert.AreEqual(0, await Coordinator(query, false, false).ProcessPendingAsync(default));
        Assert.AreEqual(0, query.Calls);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Scan_refuses_cancellation_and_ambient_transaction(bool ambient)
    {
        var query = new WorkerQuery(); var state = Substitute.For<IDataTransactionState>(); state.HasTransaction.Returns(ambient);
        var coordinator = Coordinator(query, false, true, state); using var cancelled = new CancellationTokenSource(); if (!ambient) cancelled.Cancel();
        if (ambient) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.ProcessPendingAsync(default));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => coordinator.ProcessPendingAsync(cancelled.Token));
        Assert.AreEqual(0, query.Calls);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Scan_rechecks_cancellation_and_transaction_after_wait(bool ambient)
    {
        var query = new WorkerQuery(); var state = Substitute.For<IDataTransactionState>(); using var cancelled = new CancellationTokenSource();
        query.Scan = (_, _) =>
        { if (ambient) state.HasTransaction.Returns(true); else cancelled.Cancel(); return Array.Empty<AiKnowledgeIndexWorkerRecord>(); };
        var coordinator = Coordinator(query, false, true, state);
        if (ambient) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.ProcessPendingAsync(cancelled.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => coordinator.ProcessPendingAsync(cancelled.Token));
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Scan_projects_only_bounded_original_task_metadata(bool mysql)
    {
        var sql = mysql ? AiKnowledgeIndexWorkerSql.ScanMySql : AiKnowledgeIndexWorkerSql.ScanSqlServer;
        Assert.AreEqual(SqlDataScope.Global, sql.Scope);
        foreach (var fragment in new[] { "fn_ai_knowledge_index_session", "binding.ActorUserId = knowledge.OwnerUserId", "binding.TenantId = knowledge.TenantId",
            "AttemptCount < 3", "LeaseExpiresAtUtc <= @Now", "'text/plain'", "'text/markdown'", "ORDER BY fn_ai_knowledge_index_task.CreatedAtUtc, fn_ai_knowledge_index_task.Id",
            mysql ? "LIMIT @BatchSize" : "TOP (@BatchSize)" }) Assert.IsTrue(sql.Text.Contains(fragment, StringComparison.Ordinal), fragment);
    }

    private static AiKnowledgeIndexWorkerCoordinator Coordinator(IQueryExecutor query, bool mysql, bool enabled, IDataTransactionState? state = null) =>
        new(query, state ?? Substitute.For<IDataTransactionState>(), Substitute.For<IServiceScopeFactory>(), Substitute.For<IClock>(),
            Options.Create(new DatabaseOptions { Provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer }),
            Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = enabled }), NullLogger<AiKnowledgeIndexWorkerCoordinator>.Instance);

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Contended_candidate_uses_child_tenant_scope_and_disposes_without_resolving_consumer(bool tenant)
    {
        var row = Row(tenant ? Guid.CreateVersion7() : null);
        var query = new WorkerQuery { Scan = (_, _) => new[] { row } };
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
        var disposed = 0; var attempts = 0;
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped(_ => Substitute.For<IDataTransactionState>());
        services.AddScoped<ICurrentTenant>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped(_ => new ScopeDisposal(() => disposed++));
        services.AddScoped(p =>
        {
            _ = p.GetRequiredService<ScopeDisposal>();
            var current = p.GetRequiredService<ICurrentTenant>();
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                attempts++; Assert.AreEqual(row.TenantId, current.Id); Assert.AreEqual(!tenant, current.IsHost);
                Assert.AreSame(tenant ? AiKnowledgeIndexTaskSql.AcquireTenant : AiKnowledgeIndexTaskSql.AcquireHost, call[0]);
                Assert.AreEqual(row.TaskId, ((Dictionary<string, object?>)call[1]!)["TaskId"]);
                return 0;
            });
            return new AiKnowledgeIndexTaskStore(query, command, current, clock, ids);
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var parent = provider.CreateScope();
        var parentTenant = Guid.CreateVersion7();
        parent.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(parentTenant, "parent", "parent"));
        var coordinator = new AiKnowledgeIndexWorkerCoordinator(query, Substitute.For<IDataTransactionState>(), provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new DatabaseOptions()), Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = true }), NullLogger<AiKnowledgeIndexWorkerCoordinator>.Instance);
        Assert.AreEqual(0, await coordinator.ProcessPendingAsync(default));
        Assert.AreEqual(1, attempts); Assert.AreEqual(1, disposed);
        Assert.AreEqual(parentTenant, parent.ServiceProvider.GetRequiredService<ICurrentTenant>().Id);
    }

    [TestMethod]
    public async Task Oversized_scan_is_rejected_before_creating_execution_scopes()
    {
        var query = new WorkerQuery { Scan = (_, _) => Enumerable.Range(0, 5).Select(_ => Row(null)).ToArray() };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Coordinator(query, false, true).ProcessPendingAsync(default));
    }

    private static AiKnowledgeIndexWorkerRecord Row(Guid? tenantId) => new()
    {
        TaskId = Guid.CreateVersion7(), TenantId = tenantId, KnowledgeBaseId = Guid.CreateVersion7(), DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
        OwnerUserId = Guid.CreateVersion7(), DocumentVersion = 1, KnowledgeBaseVersion = 1, ContentType = "text/plain",
    };

    [TestMethod] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)]
    public async Task Malformed_metadata_never_creates_an_execution_scope(int invalid)
    {
        var valid = Row(null);
        var row = new AiKnowledgeIndexWorkerRecord
        {
            TaskId = invalid == 0 ? Guid.Empty : valid.TaskId, KnowledgeBaseId = invalid == 1 ? Guid.Empty : valid.KnowledgeBaseId,
            DocumentId = invalid == 2 ? Guid.Empty : valid.DocumentId, VersionId = invalid == 3 ? Guid.Empty : valid.VersionId,
            OwnerUserId = invalid == 4 ? Guid.Empty : valid.OwnerUserId, TenantId = invalid == 5 ? Guid.Empty : null,
            DocumentVersion = invalid == 6 ? 0 : 1, KnowledgeBaseVersion = invalid == 7 ? 0 : 1,
        };
        var query = new WorkerQuery { Scan = (_, _) => new[] { row } };
        Assert.AreEqual(0, await Coordinator(query, false, true).ProcessPendingAsync(default)); Assert.AreEqual(1, query.Calls);
    }

    [TestMethod]
    public async Task Disabled_hosted_processor_finishes_without_creating_scope()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        using var hosted = new AiKnowledgeIndexHostedProcessor(scopes, Options.Create(new AiKnowledgeIndexWorkerOptions()), NullLogger<AiKnowledgeIndexHostedProcessor>.Instance);
        await hosted.StartAsync(default); await hosted.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); await hosted.StopAsync(default);
        Assert.IsFalse(scopes.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Enabled_hosted_processor_runs_scoped_coordinator_and_shutdown_interrupts_poll()
    {
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var disposed = 0;
        var query = new WorkerQuery { Scan = (_, _) => { scanned.TrySetResult(); return Array.Empty<AiKnowledgeIndexWorkerRecord>(); } };
        var configuration = Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = true, PollMilliseconds = 60000 });
        var services = new ServiceCollection(); services.AddScoped(_ => new ScopeDisposal(() => disposed++));
        services.AddScoped(p =>
        {
            _ = p.GetRequiredService<ScopeDisposal>();
            return new AiKnowledgeIndexWorkerCoordinator(query, Substitute.For<IDataTransactionState>(), p.GetRequiredService<IServiceScopeFactory>(),
                Substitute.For<IClock>(), Options.Create(new DatabaseOptions()), configuration, NullLogger<AiKnowledgeIndexWorkerCoordinator>.Instance);
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var hosted = new AiKnowledgeIndexHostedProcessor(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<AiKnowledgeIndexHostedProcessor>.Instance);
        await hosted.StartAsync(default); await scanned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await hosted.StopAsync(deadline.Token);
        Assert.IsTrue(hosted.ExecuteTask!.IsCompletedSuccessfully); Assert.AreEqual(1, query.Calls); Assert.AreEqual(1, disposed);
    }

    private sealed class ScopeDisposal(Action dispose) : IDisposable { public void Dispose() => dispose(); }

    // 泛型内部投影不能由动态代理自动造值；夹具只替换执行器 Port，不扩大生产类型可见性。
    private sealed class WorkerQuery : IQueryExecutor
    {
        internal int Calls;
        internal Func<SqlStatement, object?, IReadOnlyList<AiKnowledgeIndexWorkerRecord>>? Scan;
        internal IReadOnlyList<IdentityAuthorizationRow>? Grants;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (typeof(T) == typeof(IdentityAuthorizationRow)) return Task.FromResult((IReadOnlyList<T>)(object)Grants!);
            Assert.AreEqual(typeof(AiKnowledgeIndexWorkerRecord), typeof(T));
            return Task.FromResult((IReadOnlyList<T>)(object)(Scan?.Invoke(statement, parameters) ?? Array.Empty<AiKnowledgeIndexWorkerRecord>()));
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("Contended acquisition must not read or resolve execution.");
    }
}
