using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 HTTP、Identity、Files 与 Worker Profile 的本地文本来源链路；不声明付费模型或 Native 验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionWorkbenchApiTests
{
    [TestMethod]
    public Task SqlServer_workbench_preparation_survives_worker_restart_and_preserves_human_sources() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_workbench_preparation_survives_worker_restart_and_preserves_human_sources() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var failures = new ConcurrentQueue<Exception>();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IApiResultMapper>(p => new DiagnosticMapper(ActivatorUtilities.CreateInstance<StandardApiResultMapper>(p), failures))));
        await factory.InitializeAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        var beforeBudget = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        var root = factory.Services.GetRequiredService<IConfiguration>()["Files:Local:RootPath"]!;
        var permissions = new[] { AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Update, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead,
            AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeCorrectionPermissions.Read, AiKnowledgeCorrectionPermissions.Prepare, "tenancy.tenants.switch" };
        foreach (var scoped in new[] { false, true })
        {
            var owner = await factory.CreateHostIdentityAsync($"correction-workbench-{Guid.NewGuid():N}", permissions);
            using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
            using var client = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
            Guid? tenantId = null; var accessToken = owner.AccessToken;
            if (scoped)
            {
                tenantId = (await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client)).Id;
                accessToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenantId.Value);
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var createdBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("工作台准备", null));
            Assert.AreEqual(HttpStatusCode.Created, createdBase.StatusCode, await createdBase.Content.ReadAsStringAsync());
            var knowledge = (await createdBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var documentsPath = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents";
            using var createdDoc = await client.PostAsJsonAsync(documentsPath, new CreateAiKnowledgeDocumentRequest("原文不变", null));
            Assert.AreEqual(HttpStatusCode.Created, createdDoc.StatusCode, await createdDoc.Content.ReadAsStringAsync());
            var document = (await createdDoc.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
            var path = documentsPath + "/" + document.Id;
            var text = "完整原文 😀\r\n" + new string('文', 2500);
            using var uploaded = await client.PostAsync(path + $"/versions?version=1&requestKey={Guid.CreateVersion7()}&fileName=source.txt", new StringContent(text, Encoding.UTF8, "text/plain"));
            Assert.AreEqual(HttpStatusCode.Accepted, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync());
            var version = (await uploaded.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.VersionId;
            document = (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(path))!;
            var correctionPath = path + $"/versions/{version}/corrections";
            using (var empty = await client.GetAsync(correctionPath + "/workspace")) Assert.AreEqual(HttpStatusCode.NotFound, empty.StatusCode);
            using (var injected = await client.PostAsJsonAsync(correctionPath + "/prepare", new { requestKey = Guid.CreateVersion7(), documentVersion = document.Version, parserRuntimeId = "client" }))
                Assert.AreEqual(HttpStatusCode.BadRequest, injected.StatusCode);
            using (var invalid = await client.GetAsync(correctionPath + "/workspace?block=0&block=1")) Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            var request = new AiKnowledgeCorrectionPrepareRequest(Guid.CreateVersion7(), document.Version);
            var queued = await Prepare(request); Assert.AreEqual("queued", queued.State); Assert.IsNull(queued.SourceId); Assert.AreEqual(0, queued.AttemptCount);
            Assert.AreEqual(queued.Id, (await client.GetFromJsonAsync<AiKnowledgeCorrectionPreparationResponse>(correctionPath + "/preparations/latest"))!.Id, "关闭页面后通过权威最新任务恢复，不依赖本地保存的任务身份。");
            var replay = await Prepare(request); Assert.AreEqual(queued, replay, "响应丢失后按完整原身份返回相同行。");
            using (var competing = await client.PostAsJsonAsync(correctionPath + "/prepare", request with { RequestKey = Guid.CreateVersion7() }))
                Assert.AreEqual(HttpStatusCode.Conflict, competing.StatusCode);
            var snapshot = await db.QuerySingleAsync<string>("SELECT RequestHash FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { queued.Id });
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot));
            Assert.AreEqual(0, await SourceCount());
            await RunWorkerUntil(queued.Id, "completed");
            var completed = (await client.GetFromJsonAsync<AiKnowledgeCorrectionPreparationResponse>(correctionPath + "/preparations/" + queued.Id))!;
            Assert.IsNotNull(completed.SourceId); Assert.AreEqual(1, completed.AttemptCount); Assert.AreEqual("completed", completed.State);
            using (var status = await client.GetAsync(correctionPath + "/preparations/" + queued.Id))
            {
                Assert.IsTrue(status.Headers.CacheControl!.NoStore);
                var json = await status.Content.ReadAsStringAsync();
                Assert.IsFalse(json.Contains("securityStamp", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(json.Contains("sessionId", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(json.Contains("leaseToken", StringComparison.OrdinalIgnoreCase));
            }
            var workspace = (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(correctionPath + "/workspace"))!;
            Assert.AreEqual(completed.SourceId, workspace.SourceId); Assert.AreEqual(2, workspace.TotalBlocks);
            Assert.AreEqual("original", workspace.Block.Parts[0].ContentKind); Assert.AreEqual(text[..2048], workspace.Block.Parts[0].OriginalText);
            Assert.AreEqual(workspace.Block.Parts[0].Text, workspace.Block.Parts[0].OriginalText);
            Assert.AreEqual("document", workspace.Block.Parts[0].SourceLocation!.Kind); Assert.IsFalse(workspace.IsSourceView);
            var next = (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(correctionPath + "/workspace?block=1"))!;
            Assert.AreEqual(workspace.SourceId, next.SourceId); Assert.AreEqual(text[2048..], next.Block.Parts[0].Text);
            using (var invalid = await client.GetAsync(correctionPath + "/workspace?block=2")) Assert.AreEqual(HttpStatusCode.NotFound, invalid.StatusCode);
            using var guest = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
            using (var unauthorized = await guest.GetAsync(correctionPath + "/workspace")) Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            // 内部真实保存建立人工头；本批不声明尚未接线的公开编辑接口。
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var writer = sp.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } tid) writer.SetTenant(new(tid, "correction", "校对")); else writer.SetHost();
            var binding = new AiKnowledgeCorrectionStore.Binding(knowledge.Id, document.Id, version, owner.UserId, document.Version);
            var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            var original = (await store.ReadSourceAsync(binding, workspace.SourceId, default))!;
            var parsed = AiKnowledgeCorrectionSnapshot.Verify(original, null, AiKnowledgeParsedFormat.PlainText, original.FileHash, Encoding.UTF8.GetByteCount(text), AiKnowledgeParsePolicy.Default(AiKnowledgeParsedFormat.PlainText), default)!;
            var first = parsed.Document.Blocks[0]; var draft = parsed.Document.Replace(first.Id, first.Parts[0].Id, "人工校对保留完整原锚点", default).Append(null, "明确补写", Guid.CreateVersion7, default);
            var saved = await store.SaveAsync(binding, original.Id, 0, Guid.CreateVersion7(), Hash("save"), "真实本地人工头", draft, original.ParserRuntimeId, default);
            Assert.IsTrue(saved.IsSuccess); binding = binding with { DocumentVersion = binding.DocumentVersion + 1 };
            request = new(Guid.CreateVersion7(), binding.DocumentVersion);
            var reparse = await Prepare(request);
            await RunWorkerUntil(reparse.Id, "completed");
            var reparseStatus = (await client.GetFromJsonAsync<AiKnowledgeCorrectionPreparationResponse>(correctionPath + "/preparations/" + reparse.Id))!;
            var current = (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(correctionPath + "/workspace"))!;
            Assert.AreEqual(saved.Value!.Id, current.RevisionId); Assert.AreEqual("corrected", current.Block.Parts[0].ContentKind);
            Assert.AreEqual(text[..2048], current.Block.Parts[0].OriginalText); Assert.AreEqual("人工校对保留完整原锚点", current.Block.Parts[0].Text);
            var independent = (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(correctionPath + "/workspace?sourceId=" + reparseStatus.SourceId))!;
            Assert.IsTrue(independent.IsSourceView); Assert.IsNull(independent.RevisionId); Assert.AreEqual("original", independent.Block.Parts[0].ContentKind);
            var supplement = (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(correctionPath + "/workspace?block=2"))!.Block.Parts.Single();
            Assert.AreEqual("supplement", supplement.ContentKind); Assert.IsNull(supplement.AnchorId); Assert.IsNull(supplement.OriginalText); Assert.IsNull(supplement.SourceLocation);
            using (var unchangedFile = await client.GetAsync(path + $"/versions/{version}/source")) Assert.AreEqual(text, await unchangedFile.Content.ReadAsStringAsync());
            Assert.AreEqual(2, await SourceCount());

            // 明确数据库租约夹具验证迟到写入与运行时漂移；实际成功解析由上方真实 Worker 完成。
            var late = await Prepare(new(Guid.CreateVersion7(), binding.DocumentVersion));
            var queue = sp.GetRequiredService<AiKnowledgeCorrectionPreparationQueue>();
            var held = await queue.AcquireAsync(binding, late.Id, "managed:utf8-text-v1", default); Assert.IsNotNull(held);
            var lease = new AiKnowledgeCorrectionPreparationLease(held.Id, held.LeaseToken!.Value, held.RequestHash, held.ParserRuntimeId!);
            var failing = new AiKnowledgeCorrectionStore(sp.GetRequiredService<IQueryExecutor>(),
                new FailAfterCompletion(sp.GetRequiredService<ICommandExecutor>()), sp.GetRequiredService<ICommandTransaction>(),
                sp.GetRequiredService<IDataTransactionState>(), sp.GetRequiredService<ICurrentTenant>(), sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<IIdGenerator>(), sp.GetRequiredService<IOptions<DatabaseOptions>>());
            await Assert.ThrowsExactlyAsync<IOException>(() => failing.CreateSourceAsync(binding, held.Id, Hash("atomic"), parsed.Document.Source, held.ParserRuntimeId!, default, lease));
            Assert.AreEqual(2, await SourceCount()); Assert.AreEqual("running", (await queue.ReadAsync(binding, late.Id, default))!.StateKey);
            Assert.IsNull((await queue.ReadAsync(binding, late.Id, default))!.SourceId, "任务完成写后故障必须连同来源一起回滚。");
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_preparation SET LeaseExpiresAtUtc=@Past WHERE Id=@Id", new { Past = DateTime.UtcNow.AddMinutes(-1), held.Id });
            var attempted = await store.CreateSourceAsync(binding, held.Id, Hash("late"), parsed.Document.Source, held.ParserRuntimeId!, default, lease);
            Assert.IsFalse(attempted.IsSuccess); Assert.AreEqual(2, await SourceCount());
            Assert.IsNull(await queue.AcquireAsync(binding, late.Id, "fixture:changed-runtime", default));
            Assert.AreEqual("failed", (await queue.ReadAsync(binding, late.Id, default))!.StateKey);
            Assert.AreEqual("managed:utf8-text-v1", (await queue.ReadAsync(binding, late.Id, default))!.ParserRuntimeId);
            var stale = await Prepare(new(Guid.CreateVersion7(), binding.DocumentVersion));
            using (var changed = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeDocumentRequest("准备中更新元数据", null, binding.DocumentVersion)))
                Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());
            binding = binding with { DocumentVersion = binding.DocumentVersion + 1 };
            var exhausted = await Prepare(new(Guid.CreateVersion7(), binding.DocumentVersion));
            Assert.AreEqual("cancelled", (await queue.ReadAsync(binding, stale.Id, default))!.StateKey);
            Assert.AreEqual("ai.knowledge.correction_preparation_stale", (await queue.ReadAsync(binding, stale.Id, default))!.FailureCode);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_preparation SET StateKey='running',AttemptCount=3,LeaseToken=@Lease,LeaseExpiresAtUtc=@Past,ParserRuntimeId='managed:utf8-text-v1' WHERE Id=@Id",
                new { Lease = Guid.CreateVersion7(), Past = DateTime.UtcNow.AddMinutes(-1), exhausted.Id });
            await RunWorkerUntil(exhausted.Id, "failed");
            Assert.AreEqual(3, (await queue.ReadAsync(binding, exhausted.Id, default))!.AttemptCount);
            var revoked = await Prepare(new(Guid.CreateVersion7(), binding.DocumentVersion));
            var session = await db.QuerySingleAsync<Guid>("SELECT SessionId FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { revoked.Id });
            await db.ExecuteAsync("UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now WHERE Id=@Id", new { Now = DateTime.UtcNow, Id = session });
            await RunWorkerUntil(revoked.Id, "failed");
            Assert.AreEqual(2, await SourceCount()); Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE VersionId=@Id", new { Id = version }));

            async Task<AiKnowledgeCorrectionPreparationResponse> Prepare(AiKnowledgeCorrectionPrepareRequest input)
            {
                using var response = await client.PostAsJsonAsync(correctionPath + "/prepare", input);
                if (response.StatusCode == HttpStatusCode.InternalServerError && failures.TryDequeue(out var failure)) ExceptionDispatchInfo.Capture(failure).Throw();
                Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
                Assert.IsTrue(response.Headers.CacheControl!.NoStore); Assert.IsNotNull(response.Headers.Location);
                return (await response.Content.ReadFromJsonAsync<AiKnowledgeCorrectionPreparationResponse>())!;
            }
            async Task RunWorkerUntil(Guid id, string expected)
            {
                using var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection, new Dictionary<string, string?>
                {
                    [$"{AiKnowledgeIndexWorkerOptions.SectionName}:Enabled"] = "false", [$"{AiKnowledgeIndexWorkerOptions.SectionName}:CorrectionPreparationEnabled"] = "true",
                    [$"{AiKnowledgeIndexWorkerOptions.SectionName}:BatchSize"] = "2", [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"] = "250"
                }, "CorrectionPreparationRestart", nameof(AiKnowledgeIndexHostedProcessor), root);
                await worker.StartAsync();
                try
                {
                    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    while (await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { Id = id }) != expected) await Task.Delay(100, wait.Token);
                }
                finally { await worker.StopAsync(); }
            }
            Task<int> SourceCount() => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_source WHERE VersionId=@Id", new { Id = version });
        }
        Assert.AreEqual(beforeBudget, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        for (var i = 0; i < 2; i++)
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "340_AiKnowledgeCorrectionPreparations.sql", provider == DatabaseProvider.SqlServer);
            await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connection).MigrateAsync();
        }
        Assert.AreEqual(12, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_preparation"));
    }
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    /// <summary>保留正式 HTTP 错误响应，仅让测试失败时能定位真实异常；不改变生产日志脱敏。</summary>
    private sealed class DiagnosticMapper(IApiResultMapper inner, ConcurrentQueue<Exception> failures) : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext context) => inner.Map(result, context);
        public IResult MapException(Exception exception, HttpContext context) { failures.Enqueue(exception); return inner.MapException(exception, context); }
    }
    private sealed class FailAfterCompletion(ICommandExecutor inner) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var changed = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (statement.Name.StartsWith("ai.knowledge_correction_preparation.complete_", StringComparison.Ordinal)) throw new IOException("明确注入来源与任务提交前故障。");
            return changed;
        }
    }
}
