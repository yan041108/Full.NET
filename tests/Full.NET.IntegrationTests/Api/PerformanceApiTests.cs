using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Notifications;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Regions.Features.ManageAdministrativeRegions;
using Full.NET.Modules.Regions.Persistence;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实 SQL Server 上的性能修复与隔离回归。</summary>
[TestClass]
public sealed class PerformanceApiSqlServerTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public Task Performance_fixes_preserve_authority_scope_and_leases() =>
        PerformanceAssertions.VerifyAsync(DatabaseProvider.SqlServer, TestContext.WriteLine);
}

/// <summary>与 SQL Server 使用同一数据规模与断言的 MySQL 回归。</summary>
[TestClass]
public sealed class PerformanceApiMySqlTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public Task Performance_fixes_preserve_authority_scope_and_leases() =>
        PerformanceAssertions.VerifyAsync(DatabaseProvider.MySql, TestContext.WriteLine);
}

internal static class PerformanceAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider, Action<string> evidence)
    {
        var reads = new ConcurrentQueue<string>();
        var clock = new AdvancingClock();
        var database = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, database, configureTestServices: services =>
        {
            NotificationProfileBindingAssertions.ConfigureTestServices(services);
            services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock);
            var original = services.Last(d => d.ServiceType == typeof(IQueryExecutor));
            services.RemoveAll<IQueryExecutor>();
            services.AddScoped<IQueryExecutor>(sp => new CountingQueries((IQueryExecutor)(original.ImplementationFactory?.Invoke(sp)
                ?? original.ImplementationInstance ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), reads));
        });
        await factory.InitializeAsync();
        evidence($"provider={provider} runtime={Environment.Version} cpu={Environment.ProcessorCount} configuration=Release");
        await VerifyAuthorizationAsync(factory, reads, evidence);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        await VerifyRecoveryAsync(scope.ServiceProvider, provider, database, evidence);
        await VerifyRegionsAsync(scope.ServiceProvider, provider, database, evidence);
        // 先验证真实活动任务，后面的游标夹具会故意留下不可执行租户的排队记录。
        await PerformanceImportExecutionAssertions.VerifyAsync(factory, provider, evidence);
        await PerformanceFollowupAssertions.VerifyAsync(factory, provider, database, evidence);
        await NotificationDeliveryWorkerAssertions.VerifyConcurrencyAsync(factory, evidence, () => clock.Advance(TimeSpan.FromSeconds(50)));
    }

    private static async Task VerifyAuthorizationAsync(FullNetApiFactory factory, ConcurrentQueue<string> reads, Action<string> evidence)
    {
        string[] required = ["regions.administrative_regions.read", "regions.administrative_regions.create",
            "regions.administrative_regions.update", "regions.administrative_regions.delete"];
        var identity = await factory.CreateHostIdentityAsync($"perf-{Guid.CreateVersion7():N}", required);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        http.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new JsonWebToken(identity.AccessToken).Claims, "test")) };
        try
        {
            var auth = scope.ServiceProvider.GetRequiredService<ICurrentSessionAuthorization>();
            await auth.AuthorizeAllAsync(required);
            foreach (var batch in new[] { false, true })
            {
                List<double> samples = [];
                var count = 0;
                for (var i = 0; i < 5; i++)
                {
                    reads.Clear();
                    var watch = Stopwatch.StartNew();
                    if (batch) Assert.IsNotNull(await auth.AuthorizeAllAsync(required));
                    else foreach (var permission in required) Assert.IsNotNull(await auth.AuthorizeAsync(permission));
                    watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds);
                    count = reads.Count;
                    Assert.AreEqual(batch ? 2 : 8, count);
                }
                evidence($"authorization batch={batch} permissions=4 samples=5 reads_per_boundary={count} {Timing(samples)} errors=0");
            }
            Assert.IsNull(await auth.AuthorizeAllAsync([required[0], "unknown.permission"]));
            // 同一权限集合在下一边界重新校验会话，不缓存先前成功。
            http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            Assert.IsNull(await auth.AuthorizeAllAsync(required));
        }
        finally { http.HttpContext = null; }
    }

    private static async Task VerifyRecoveryAsync(IServiceProvider sp, DatabaseProvider provider, string database, Action<string> evidence)
    {
        var commands = sp.GetRequiredService<ICommandExecutor>();
        var query = sp.GetRequiredService<IQueryExecutor>();
        var now = DateTimeOffset.UtcNow;
        var definition = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var form = Guid.CreateVersion7(); var formVersion = Guid.CreateVersion7();
        // 扫描夹具也遵守必需版本引用，不以空表单绕过实际双库约束。
        await commands.ExecuteAsync(WorkflowSql.InsertFormDefinition, P(("Id", form), ("TenantId", null),
            ("ScopeKey", "host"), ("TenantScopeKey", "host"), ("FormKey", "performance"),
            ("DraftSchemaJson", "{}"), ("CreatedById", actor), ("CreatedAtUtc", now)));
        await commands.ExecuteAsync(WorkflowSql.InsertFormVersion, P(("Id", formVersion), ("FormDefinitionId", form),
            ("VersionNumber", 1), ("SchemaVersion", 1), ("AdapterVersion", 1), ("ComponentCatalogVersion", 1),
            ("FormSchemaJson", "{}"), ("WebRenderSchemaJson", "{}"), ("ContentHash", new string('0', 64)),
            ("PublishedById", actor), ("PublishedAtUtc", now)));
        await commands.ExecuteAsync(WorkflowSql.InsertDefinition, P(("Id", definition), ("TenantId", null), ("ScopeKey", "host"),
            ("TenantScopeKey", "host"), ("DefinitionKey", "performance"), ("DraftId", Guid.CreateVersion7()),
            ("BusinessTitleTemplate", null), ("CreatedById", actor), ("CreatedAtUtc", now)));
        await commands.ExecuteAsync(WorkflowSql.InsertDefinitionVersion, P(("Id", version), ("DefinitionId", definition),
            ("FormVersionId", formVersion), ("VersionNumber", 1), ("SchemaVersion", 1), ("CanonicalJson", "{}"),
            ("ContentHash", "performance"), ("BusinessTitleTemplate", null), ("PublishedById", actor), ("PublishedAtUtc", now)));
        for (var i = 0; i < 36; i++)
        {
            var id = Guid.CreateVersion7(); var step = Guid.CreateVersion7();
            await commands.ExecuteAsync(WorkflowSql.InsertInstance, P(("Id", id), ("TenantId", null), ("ScopeKey", "host"),
                ("TenantScopeKey", "host"), ("DefinitionVersionId", version), ("FormVersionId", formVersion), ("BusinessType", "performance"),
                ("BusinessId", id.ToString("N")), ("BusinessTitle", null), ("StartedById", actor), ("StartedAtUtc", now)));
            await commands.ExecuteAsync(new SqlStatement("test.performance.expire", "UPDATE fn_workflow_instance SET LeaseExpiresAtUtc=@Old WHERE Id=@Id", SqlDataScope.Global), P(("Old", now.AddHours(-1)), ("Id", id)));
            await commands.ExecuteAsync(WorkflowSql.InsertStep, P(("Id", step), ("InstanceId", id), ("NodeKey", "approval"),
                ("AssignedUserId", actor), ("ExecutionSequence", 1), ("StartedAtUtc", now)));
        }
        var options = Microsoft.Extensions.Options.Options.Create(new WorkflowRecoveryWorkerOptions { BatchSize = 7 });
        var scanner = ActivatorUtilities.CreateInstance<WorkflowRecoveryScanner>(sp, options);
        var found = 0;
        for (var i = 0; i < 7; i++)
        {
            var count = await scanner.ScanAsync(default);
            Assert.IsTrue(count <= 7); found += count;
        }
        Assert.AreEqual(36, found);
        Assert.AreEqual(108L, await query.QuerySingleOrDefaultAsync<long>(new SqlStatement("test.performance.recovery_count",
            "SELECT COUNT(*) FROM fn_workflow_recovery_task", SqlDataScope.Global)));
        await commands.ExecuteAsync(new SqlStatement("test.performance.occupancy_states",
            "UPDATE fn_workflow_recovery_task SET StatusKey=CASE KindKey WHEN 'expired_lease' THEN 'failed' WHEN 'stuck_instance' THEN 'dead_lettered' ELSE 'pending' END", SqlDataScope.Global));
        var baseline = new[] {
            "SELECT instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id AS InstanceId, NULLIF(instance.Id,instance.Id) AS StepId FROM fn_workflow_instance instance WHERE instance.StatusKey='active' AND instance.LeaseExpiresAtUtc IS NOT NULL AND instance.LeaseExpiresAtUtc<=@Now",
            "SELECT instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id AS InstanceId, NULLIF(instance.Id,instance.Id) AS StepId FROM fn_workflow_instance instance WHERE instance.StatusKey='active' AND (instance.LeaseExpiresAtUtc IS NULL OR instance.LeaseExpiresAtUtc<=@Now) AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo todo WHERE todo.InstanceId=instance.Id AND todo.StatusKey='active')",
            "SELECT instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id AS InstanceId, step.Id AS StepId FROM fn_workflow_instance instance INNER JOIN fn_workflow_step step ON step.InstanceId=instance.Id WHERE instance.StatusKey='active' AND step.StatusKey='active' AND step.NodeTypeKey='human.approval' AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo todo WHERE todo.StepId=step.Id AND todo.StatusKey='active')" };
        List<double> oldSamples = [], newSamples = [];
        for (var sample = 0; sample < 6; sample++)
        {
            var watch = Stopwatch.StartNew(); var noops = 0;
            for (var kind = 0; kind < 3; kind++)
                foreach (var row in await query.QueryAsync<WorkflowRecoveryScanCandidate>(new SqlStatement("test.performance.old_scan", baseline[kind], SqlDataScope.Global), P(("Now", now))))
                {
                    Assert.AreEqual(0, await commands.ExecuteAsync(WorkflowRecoverySql.InsertOpenTask, P(("Id", Guid.CreateVersion7()),
                        ("TenantId", row.TenantId), ("ScopeKey", row.ScopeKey), ("TenantScopeKey", row.TenantScopeKey),
                        ("InstanceId", row.InstanceId), ("StepId", row.StepId), ("KindKey", new[] { "expired_lease", "stuck_instance", "incomplete_step" }[kind]),
                        ("CreatedAtUtc", now), ("UpdatedAtUtc", now)))); noops++;
                }
            watch.Stop(); if (sample > 0) oldSamples.Add(watch.Elapsed.TotalMilliseconds);
            Assert.AreEqual(108, noops);
            watch.Restart(); Assert.AreEqual(0, await scanner.ScanAsync(default)); watch.Stop();
            if (sample > 0) newSamples.Add(watch.Elapsed.TotalMilliseconds);
        }
        evidence($"workflow instances=36 steps=36 open_tasks=108 baseline_reads=3 baseline_noop_inserts=108 candidate_reads=3 candidate_inserts=0 samples=5 baseline_{Timing(oldSamples)} candidate_{Timing(newSamples)} errors=0");
        await CapturePlanAsync(provider, database, provider == DatabaseProvider.MySql ? WorkflowRecoverySql.ScanStuckInstancesMySql : WorkflowRecoverySql.ScanStuckInstancesSqlServer,
            P(("Now", now.UtcDateTime), ("KindKey", "stuck_instance"), ("BatchSize", 7)), evidence);
    }

    private static async Task VerifyRegionsAsync(IServiceProvider sp, DatabaseProvider provider, string database, Action<string> evidence)
    {
        var command = sp.GetRequiredService<ICommandExecutor>(); var query = sp.GetRequiredService<IQueryExecutor>();
        var root = Guid.CreateVersion7();
        await InsertRegionAsync(root, null, "p-root", 1);
        for (var i = 0; i < 500; i++)
        {
            var parent = Guid.CreateVersion7();
            await InsertRegionAsync(parent, root, $"p-{i}", 2);
            await InsertRegionAsync(Guid.CreateVersion7(), parent, $"c-{i}", 3);
        }
        var localParent = Guid.CreateVersion7();
        await InsertRegionAsync(localParent, null, "local", 1);
        var localChild = Guid.CreateVersion7();
        await InsertRegionAsync(localChild, localParent, "local-child", 2);
        await InsertRegionAsync(Guid.CreateVersion7(), localChild, "local-leaf", 3);
        var service = sp.GetRequiredService<AdministrativeRegionQueryService>();
        List<double> oldSamples = [], newSamples = [];
        var total = 0;
        for (var sample = 0; sample < 6; sample++)
        {
            var watch = Stopwatch.StartNew();
            var all = await query.QueryAsync<AdministrativeRegionTreeRecord>(new SqlStatement("test.performance.old_tree",
                "SELECT Id, ParentId, Code, Name, Level, DisplayOrder FROM fn_regions_administrative_region ORDER BY DisplayOrder, Name, Code, Id", SqlDataScope.HostOnly));
            watch.Stop(); total = all.Count; if (sample > 0) oldSamples.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart(); var result = await service.GetTreeAsync(localParent, 1); watch.Stop();
            Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, result.Value!.Count);
            Assert.AreEqual(localChild, result.Value[0].Id); Assert.AreEqual(0, result.Value[0].Children.Count);
            if (sample > 0) newSamples.Add(watch.Elapsed.TotalMilliseconds);
        }
        var deep = await service.GetTreeAsync(localParent, 2);
        Assert.AreEqual(1, deep.Value![0].Children.Count);
        var children = await service.ListChildrenAsync(localParent);
        Assert.IsTrue(children.Value!.Single().HasChildren);
        var statement = provider == DatabaseProvider.MySql ? AdministrativeRegionSql.TreeMySql : AdministrativeRegionSql.TreeSqlServer;
        var actualRows = await query.QueryAsync<AdministrativeRegionTreeRecord>(statement, P(("ParentId", localParent), ("MaxDepth", 1), ("MaxNodes", 10001)));
        Assert.AreEqual(1, actualRows.Count);
        var boundedRows = await query.QueryAsync<AdministrativeRegionTreeRecord>(statement,
            P(("ParentId", root), ("MaxDepth", 5), ("MaxNodes", 2)));
        Assert.AreEqual(2, boundedRows.Count, "大分支须在数据库阶段遵守节点预算。");
        evidence($"regions fixture_nodes=1004 baseline_materialized={total} candidate_materialized=1 depth=1 samples=5 baseline_{Timing(oldSamples)} candidate_{Timing(newSamples)} errors=0");
        await CapturePlanAsync(provider, database, statement, P(("ParentId", localParent), ("MaxDepth", 1), ("MaxNodes", 10001)), evidence);

        async Task InsertRegionAsync(Guid id, Guid? parent, string code, int level) =>
            _ = await command.ExecuteAsync(AdministrativeRegionSql.Insert, P(("Id", id), ("ParentId", parent), ("Code", code),
                ("Name", code), ("ShortName", null), ("MergerName", null), ("ZipCode", null), ("CityCode", null), ("Level", level),
                ("RegionType", null), ("PinYin", null), ("Longitude", null), ("Latitude", null), ("DisplayOrder", 0),
                ("Remark", null), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1L)));
    }

    // 计划保存在本轮 TRX 中；不修改索引，不以小样本延迟推断生产容量。
    internal static async Task CapturePlanAsync(DatabaseProvider provider, string database, SqlStatement statement,
        Dictionary<string, object?> parameters, Action<string> evidence)
    {
        await using System.Data.Common.DbConnection connection = provider == DatabaseProvider.MySql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(database, MySqlGuidStorageMode.Binary16, false))
            : new SqlConnection(database);
        await connection.OpenAsync();
        evidence($"server {provider}: {connection.ServerVersion}");
        // SQL Server 读取刚经正式执行器运行的编译计划，避免重放参数或会话 SET 状态改变诊断语义。
        // 按当前测试数据库与完整 SQL 匹配；找不到计划必须失败，不能用空结果冒充已采集。
        var plan = provider == DatabaseProvider.SqlServer
            ? await connection.QueryFirstOrDefaultAsync<string>("""
                SELECT TOP (1) CONVERT(nvarchar(max), compiled.query_plan)
                FROM sys.dm_exec_query_stats AS stats
                CROSS APPLY sys.dm_exec_sql_text(stats.sql_handle) AS text
                CROSS APPLY sys.dm_exec_query_plan(stats.plan_handle) AS compiled
                WHERE compiled.dbid = DB_ID()
                  AND CHARINDEX(@SqlText, text.text) > 0
                  AND compiled.query_plan IS NOT NULL
                ORDER BY stats.last_execution_time DESC
                """, new { SqlText = statement.Text })
            : await connection.QueryFirstAsync<string>("EXPLAIN FORMAT=JSON " + statement.Text, new DynamicParameters(parameters));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plan), $"未采集到 {statement.Name} 的编译计划。");
        evidence($"plan {statement.Name}: {plan}");
    }

    private static string Timing(List<double> samples)
    {
        var sorted = samples.Order().ToArray();
        return $"p50_ms={sorted[sorted.Length / 2]:F2} p95_ms={sorted[^1]:F2} p99_ms={sorted[^1]:F2}";
    }
    private static Dictionary<string, object?> P(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
    private sealed class AdvancingClock : IClock
    {
        private long _offsetTicks;
        // 保留真实时间推进，避免 datetime2(6) 向上舍入后 NextAttempt 永远晚于冻结时钟。
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref _offsetTicks, duration.Ticks);
    }

    private sealed class CountingQueries(IQueryExecutor inner, ConcurrentQueue<string> reads) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { reads.Enqueue(statement.Name); return inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken); }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { reads.Enqueue(statement.Name); return inner.QueryAsync<T>(statement, parameters, cancellationToken); }
    }
}
