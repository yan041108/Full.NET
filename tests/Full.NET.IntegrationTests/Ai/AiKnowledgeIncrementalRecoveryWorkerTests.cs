using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库覆盖全局锚点扫描与独立范围写入；补偿不创建任何收费任务或会话。</summary>
[TestClass]
public sealed class AiKnowledgeIncrementalRecoveryWorkerTests
{
    [TestMethod]
    public Task SqlServer_worker_recovers_history_reenabled_bases_and_latest_confirmed_sources() => Verify(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_worker_recovers_history_reenabled_bases_and_latest_confirmed_sources() => Verify(DatabaseProvider.MySql);

    private static async Task Verify(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        var owner = await factory.CreateHostIdentityAsync($"auto-recovery-{Guid.CreateVersion7():N}",
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Read,
                AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.VersionsRead, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        // 切换租户会使原 Host 令牌失效；先完成 Host 来源，随后只使用新租户令牌，不绕过会话校验。
        var first = await Create(host, "host-first"); var third = await Create(host, "host-last");
        var deleted = await Create(host, "deleted"); var pending = await Create(host, "pending");
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var activeTenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, activeTenant.Id, owner.UserId);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, activeTenant.Id));
        var second = await Create(tenantClient, "tenant");
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        // 保留真实导入历史；只安排未确认、墓碑与停用状态，模拟旧部署未拥有来源意图。
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@Id", new { Id = first.Knowledge });
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = deleted.Document });
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='pending_confirmation' WHERE VersionId=@VersionId OR DocumentId=@DocumentId", new { VersionId = third.Latest, DocumentId = pending.Document });
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change"));
        using var cursor = new AiKnowledgeIncrementalRecoveryCursor();
        await Drain(cursor);
        Assert.AreEqual(2, await Count());
        Assert.AreEqual(2, await Number(second.Document)); Assert.AreEqual(1, await Number(third.Document));
        Assert.IsNull(cursor.Id);

        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=1 WHERE Id=@Id", new { Id = first.Knowledge });
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='completed' WHERE VersionId=@VersionId", new { VersionId = third.Latest });
        // 独立游标模拟两个实例与重启；共享文档锁必须收敛为单条最新来源，不依赖进程内互斥。
        using var restarted = new AiKnowledgeIncrementalRecoveryCursor();
        using var raceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var race = new ScanBarrier(raceDeadline.Token);
        await Task.WhenAll(Drain(cursor, race), Drain(restarted, race));
        Assert.AreEqual(2, race.Candidates.Count); Assert.AreEqual(race.Candidates[0], race.Candidates[1]);
        Assert.AreEqual(3, await Count()); Assert.AreEqual(2, await Number(first.Document)); Assert.AreEqual(2, await Number(third.Document));
        var projection = await db.QueryAsync<Guid>("SELECT DocumentId FROM fn_ai_knowledge_index_change");
        CollectionAssert.AreEquivalent(new[] { first.Document, second.Document, third.Document }, projection.ToArray());
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));

        async Task Drain(AiKnowledgeIncrementalRecoveryCursor position, ScanBarrier? gate = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            for (var batch = 0; batch < 12; batch++)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); tenant.SetHost();
                var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
                var worker = ActivatorUtilities.CreateInstance<AiKnowledgeIncrementalRecoveryWorker>(scope.ServiceProvider,
                    gate is null ? query : new SynchronizedScan(query, gate),
                    Options.Create(new AiKnowledgeIncrementalRecoveryWorkerOptions { Enabled = true, BatchSize = 1 }), position);
                var attempted = await worker.ProcessPendingAsync(deadline.Token);
                Assert.IsTrue(tenant.IsHost); Assert.IsNull(tenant.Id);
                if (attempted == 0) return;
                Assert.AreEqual(1, attempted);
            }
            Assert.Fail("Bounded recovery did not reach an empty page.");
        }
        Task<int> Count() => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change");
        Task<int> Number(Guid document) => db.QuerySingleAsync<int>("SELECT VersionNumber FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document });
        async Task<(Guid Knowledge, Guid Document, Guid Latest)> Create(HttpClient client, string name)
        {
            using var createdBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest(name, null));
            Assert.AreEqual(HttpStatusCode.Created, createdBase.StatusCode, await createdBase.Content.ReadAsStringAsync());
            var knowledge = (await createdBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var root = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents";
            using var createdDoc = await client.PostAsJsonAsync(root, new CreateAiKnowledgeDocumentRequest(name, null));
            Assert.AreEqual(HttpStatusCode.Created, createdDoc.StatusCode, await createdDoc.Content.ReadAsStringAsync());
            var doc = (await createdDoc.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!; var latest = Guid.Empty;
            for (var version = 1; version <= 2; version++)
            {
                using var uploaded = await client.PostAsync(root + $"/{doc.Id}/versions?version={version}&requestKey={Guid.CreateVersion7()}&fileName=source.txt", new StringContent(name + version, Encoding.UTF8, "text/plain"));
                Assert.AreEqual(HttpStatusCode.Accepted, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync());
                latest = (await uploaded.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.VersionId;
            }
            return (knowledge.Id, doc.Id, latest);
        }
    }

    // 保证两个扫描都拿到同一候选后才允许写入；不声称测量了数据库内部实际锁等待时长。
    private sealed class ScanBarrier(CancellationToken token)
    {
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<Guid> Candidates { get; } = [];
        internal Task Arrive(IReadOnlyList<AiKnowledgeSourceConfirmed> rows)
        {
            lock (Candidates)
            {
                if (Candidates.Count >= 2) return Task.CompletedTask;
                Assert.AreEqual(1, rows.Count); Candidates.Add(rows[0].DocumentId);
                if (Candidates.Count == 2) _ready.TrySetResult(true);
            }
            return _ready.Task.WaitAsync(token);
        }
    }
    private sealed class SynchronizedScan(IQueryExecutor inner, ScanBarrier barrier) : IQueryExecutor
    {
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var rows = await inner.QueryAsync<T>(statement, parameters, cancellationToken);
            await barrier.Arrive((IReadOnlyList<AiKnowledgeSourceConfirmed>)(object)rows);
            return rows;
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
    }
}
