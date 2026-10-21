using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证历史缺投影、停用恢复、分页与删除边界；所有恢复均不能派发收费任务。</summary>
[TestClass]
public sealed class AiKnowledgeIncrementalRecoveryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SqlServer_recovers_historical_and_disabled_sources_without_paid_work(bool scoped) => Verify(DatabaseProvider.SqlServer, scoped);
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task MySql_recovers_historical_and_disabled_sources_without_paid_work(bool scoped) => Verify(DatabaseProvider.MySql, scoped);

    private static async Task Verify(DatabaseProvider provider, bool scoped)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.Update,
            AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Upload,
            AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeDocumentPermissions.Delete,
            AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRecover, "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"recover-{Guid.CreateVersion7():N}", permissions);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        using var client = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
        var accessToken = owner.AccessToken;
        Guid? activeTenant = null;
        if (scoped)
        {
            var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client);
            activeTenant = tenant.Id;
            await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
            accessToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("来源恢复", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var root = $"/api/v1/ai/knowledge-bases/{knowledge.Id}"; var recover = root + "/index-changes/recover";
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        var docs = new List<AiKnowledgeDocumentResponse>();
        for (var i = 0; i < 2; i++)
        {
            using var document = await client.PostAsJsonAsync(root + "/documents", new CreateAiKnowledgeDocumentRequest($"恢复{i}", null));
            Assert.AreEqual(HttpStatusCode.Created, document.StatusCode);
            docs.Add((await document.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!);
            await Upload(docs[i], 1);
        }
        await Upload(docs[0], 2);
        // 模拟旧部署与停用期间已确认、没有当前投影的来源，保留真实导入与事件记录。
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change"));
        await VerifyRevocationDuringLockWait(factory, knowledge.Id, owner.UserId, activeTenant);
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change");
        var first = await Recover(new(null, 1));
        Assert.AreEqual(1, first.Scanned); Assert.AreEqual(1, first.Updated); Assert.IsNotNull(first.NextDocumentId);
        var last = await Recover(new(first.NextDocumentId, 1));
        Assert.AreEqual(1, last.Scanned); Assert.AreEqual(1, last.Updated); Assert.IsNull(last.NextDocumentId);
        var replay = await Recover(new()); Assert.AreEqual(2, replay.Scanned); Assert.AreEqual(0, replay.Updated);
        Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT VersionNumber FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = docs[0].Id }));
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change");
        using var disabled = await client.PutAsJsonAsync(root, new UpdateAiKnowledgeBaseRequest(knowledge.Name, null, false, knowledge.Version));
        Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        var disabledBase = (await disabled.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        using var blocked = await client.PostAsJsonAsync(recover, new AiKnowledgeIncrementalRecoveryRequest());
        Assert.AreEqual(HttpStatusCode.NotFound, blocked.StatusCode);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change"));
        using var enabled = await client.PutAsJsonAsync(root, new UpdateAiKnowledgeBaseRequest(knowledge.Name, null, true, disabledBase.Version));
        Assert.AreEqual(HttpStatusCode.OK, enabled.StatusCode);
        Assert.AreEqual(2, (await Recover(new())).Updated);
        using var deleting = new HttpRequestMessage(HttpMethod.Delete, root + $"/documents/{docs[1].Id}") { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(2)) };
        using var deleted = await client.SendAsync(deleting); Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.AreEqual(1, (await Recover(new())).Scanned);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = docs[1].Id }));
        using var invalid = await client.PostAsJsonAsync(recover, new AiKnowledgeIncrementalRecoveryRequest(null, 51));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var unknown = await client.PostAsync(recover, new StringContent("{\"tenantId\":\"" + Guid.CreateVersion7() + "\"}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var queryField = await client.PostAsJsonAsync(recover + "?tenantId=" + Guid.CreateVersion7(), new AiKnowledgeIncrementalRecoveryRequest());
        Assert.AreEqual(HttpStatusCode.BadRequest, queryField.StatusCode);
        var stranger = await factory.CreateHostIdentityAsync($"recover-stranger-{Guid.CreateVersion7():N}", permissions);
        using var other = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
        other.DefaultRequestHeaders.Authorization = new("Bearer", stranger.AccessToken);
        if (activeTenant is { } tenantId)
        {
            using var crossed = await other.PostAsJsonAsync(recover, new AiKnowledgeIncrementalRecoveryRequest());
            Assert.IsTrue(crossed.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
            await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenantId, stranger.UserId);
            using var otherHost = factory.CreateClientForHost("localhost");
            other.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(otherHost, stranger.AccessToken, tenantId));
        }
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@KnowledgeBaseId,@UserId,@Now)",
            new { Id = Guid.CreateVersion7(), KnowledgeBaseId = knowledge.Id, UserId = stranger.UserId, Now = DateTime.UtcNow });
        using var readable = await other.GetAsync(root); Assert.AreEqual(HttpStatusCode.OK, readable.StatusCode);
        // 明确可读成员仍不能借独立操作权执行所有者恢复。
        using var forbidden = await other.PostAsJsonAsync(recover, new AiKnowledgeIncrementalRecoveryRequest());
        Assert.AreEqual(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session"));
        if (!scoped && Environment.GetEnvironmentVariable("FULLNET_AI_RECOVERY_OPENAPI_OUTPUT") is { Length: > 0 } output)
        {
            using var specification = await client.GetAsync("/openapi/v1.json");
            Assert.AreEqual(HttpStatusCode.OK, specification.StatusCode);
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, provider + ".json"), await specification.Content.ReadAsStringAsync());
        }

        async Task Upload(AiKnowledgeDocumentResponse document, int version)
        {
            using var result = await client.PostAsync(root + $"/documents/{document.Id}/versions?version={version}&requestKey={Guid.CreateVersion7()}&fileName=source.txt", new StringContent("恢复来源" + version, Encoding.UTF8, "text/plain"));
            Assert.AreEqual(HttpStatusCode.Accepted, result.StatusCode, await result.Content.ReadAsStringAsync());
        }
        async Task<AiKnowledgeIncrementalRecoveryResponse> Recover(AiKnowledgeIncrementalRecoveryRequest request)
        {
            using var result = await client.PostAsJsonAsync(recover, request);
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode, await result.Content.ReadAsStringAsync());
            return (await result.Content.ReadFromJsonAsync<AiKnowledgeIncrementalRecoveryResponse>())!;
        }
    }

    // 真实文档锁负责阻塞写入；可控权威授权 Port 只用于准确安排撤权时序，不把 Identity 查询放入 AI 事务。
    private static async Task VerifyRevocationDuringLockWait(FullNetApiFactory factory, Guid knowledge, Guid actor, Guid? tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<CurrentTenantAccessor>();
        if (tenantId is { } id) tenant.SetTenant(new TenantContext(id, "acme", "Acme")); else tenant.SetHost();
        await using var blocker = AiOperationBudgetAssertions.Connection(factory); await blocker.OpenAsync();
        await using var blocking = await blocker.BeginTransactionAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorization = new RevocableSession(new(actor, tenantId, Guid.CreateVersion7()));
        var inner = services.GetRequiredService<IQueryExecutor>();
        var query = new LockWaitQuery(inner, async parameters =>
        {
            var document = (Guid)((IReadOnlyDictionary<string, object?>)parameters!)["DocumentId"]!;
            await blocker.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Title=Title WHERE Id=@Id", new { Id = document }, blocking);
        }, ready);
        var writer = ActivatorUtilities.CreateInstance<AiKnowledgeIncrementalIndexWriter>(services, query);
        var guard = new AiKnowledgePermissionGuard(authorization, services.GetRequiredService<IHttpContextAccessor>(), services.GetRequiredService<ICurrentApiKeyAuthorization>());
        var service = new AiKnowledgeIncrementalRecoveryService(query, services.GetRequiredService<IDataTransactionState>(), writer, tenant, guard, services.GetRequiredService<IOptions<DatabaseOptions>>());
        var pending = service.RecoverAsync(knowledge, actor, new(), deadline.Token);
        var released = false;
        try
        {
            await ready.Task.WaitAsync(deadline.Token);
            Assert.IsFalse(pending.IsCompleted);
            authorization.Allowed = false;
            await blocking.CommitAsync(deadline.Token);
            released = true;
            var refused = await pending;
            Assert.IsFalse(refused.IsSuccess); Assert.AreEqual("authorization.permission_denied", refused.Error!.Code);
            await using var inspect = AiOperationBudgetAssertions.Connection(factory);
            Assert.AreEqual(1, await inspect.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change"));
            Assert.AreEqual(1, query.Admitted);
            Assert.AreEqual(0, await inspect.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task"));
        }
        finally
        {
            await deadline.CancelAsync();
            if (!released) await blocking.RollbackAsync();
            try { await pending; } catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    private sealed class RevocableSession(AuthorizedSessionActor actor) : ICurrentSessionAuthorization
    {
        internal bool Allowed { get; set; } = true;
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(Allowed && permissionCodes.Count > 0 && permissionCodes.All(p => !string.IsNullOrWhiteSpace(p)) ? actor : null);
        public Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(Allowed ? actor : null);
    }

    private sealed class LockWaitQuery(IQueryExecutor inner, Func<object?, Task> block, TaskCompletionSource<bool> ready) : IQueryExecutor
    {
        internal int Admitted { get; private set; }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => inner.QueryAsync<T>(statement, parameters, cancellationToken);
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (!statement.Name.StartsWith("ai.knowledge_index_change.lock_", StringComparison.Ordinal))
                return await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            Admitted++;
            await block(parameters);
            var pending = inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            ready.TrySetResult(true);
            return await pending;
        }
    }
}
