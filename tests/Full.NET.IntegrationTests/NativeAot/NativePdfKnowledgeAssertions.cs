using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Features;
using System.Data.Common;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native HTTP、当前读者授权和 Worker 恢复闭包；合成已结算载荷不冒充文件解析或云模型派发。</summary>
internal static class NativePdfKnowledgeAssertions
{
    internal static Dictionary<string, string?> Settings(string executable) => new()
    {
        ["FullNet:Ai:KnowledgeIndexRebuild:Enabled"] = "true",
        ["FullNet:Ai:KnowledgePdf:Enabled"] = "true",
        ["FullNet:Ai:KnowledgePdf:NodeExecutablePath"] = executable,
        ["FullNet:Ai:KnowledgePdf:CliScriptPath"] = executable,
        ["FullNet:Ai:KnowledgePdf:ImageId"] = "sha256:" + new string('a', 64),
        ["FullNet:Ai:KnowledgeDocx:Enabled"] = "true",
        ["FullNet:Ai:KnowledgeDocx:NodeExecutablePath"] = executable,
        ["FullNet:Ai:KnowledgeDocx:CliScriptPath"] = executable,
        ["FullNet:Ai:KnowledgeDocx:ImageId"] = "sha256:" + new string('a', 64)
    };

    internal static async Task VerifyAsync(HttpClient client, string token, DatabaseProvider provider, string connectionString, CancellationToken cancellation,
        bool nativeWorker = false, bool docx = false)
    {
        string Claim(string name) => IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, name)!;
        var session = new SessionBindingSnapshot(Guid.Parse(Claim(FullNetIdentityClaimTypes.Subject)), null,
            Guid.Parse(Claim(FullNetIdentityClaimTypes.SessionId)), Claim(FullNetIdentityClaimTypes.SecurityStamp),
            Claim(FullNetIdentityClaimTypes.ActorScope), Claim(FullNetIdentityClaimTypes.Scope));
        var clock = new AiOperationBudgetAssertions.TestClock(); var controls = new AiKnowledgeIndexPdfVectorAssertions.WorkerControls();
        var docxControls = new AiKnowledgeIndexDocumentPersistenceAssertions.WorkerControls();
        using var factory = new FullNetApiFactory(provider, connectionString, configureTestServices: services =>
        { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); if (docx) docxControls.Register(services); else controls.Register(services); });
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        var historyChecked = false;
        var admittedTasks = new Dictionary<Guid, Guid>();
        static string PathFor(AiKnowledgeIndexTaskBinding b) => $"/api/v1/ai/knowledge-bases/{b.KnowledgeBaseId}/documents/{b.DocumentId}/versions/{b.VersionId}/index";
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content }; request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, cancellation);
        }
        async Task Require(HttpResponseMessage response, HttpStatusCode expected)
        { Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(cancellation)); }
        async Task ReadReady(AiKnowledgeIndexTaskBinding b)
        {
            using var response = await Send(HttpMethod.Get, PathFor(b)); await Require(response, HttpStatusCode.OK);
            var status = await response.Content.ReadFromJsonAsync<AiKnowledgeIndexStatusResponse>(cancellation);
            Assert.IsNotNull(status); Assert.AreEqual("ready", status.State);
            if (!nativeWorker)
            {
                // 在真实 Native API 中读取只读核对契约；沿用已完成的收费来源 证明，不产生新模型调用。
                using var reconciliation = await Send(HttpMethod.Get, PathFor(b) + "/reconciliation"); await Require(reconciliation, HttpStatusCode.OK);
                var checkedIndex = await reconciliation.Content.ReadFromJsonAsync<AiKnowledgeIndexReconciliationResponse>(cancellation);
                Assert.IsNotNull(checkedIndex); Assert.AreEqual(b.VersionId, checkedIndex.VersionId);
                Assert.AreEqual(status.TaskId, checkedIndex.TaskId); Assert.AreEqual("ready", checkedIndex.State);
                Assert.AreEqual("ready", checkedIndex.Reason); Assert.AreEqual("none", checkedIndex.RecoveryAction);
                Assert.AreEqual(b.DocumentVersion, checkedIndex.DocumentVersion); Assert.AreEqual(b.KnowledgeBaseVersion, checkedIndex.KnowledgeBaseVersion);
            }
            if (!nativeWorker && !historyChecked)
            {
                historyChecked = true;
                // 使用同一已结算来源 夹具验证真实 Native 历史读取；正文由夹具种入，不宣称云模型生成。
                await VerifyHistoryAsync(b);
            }
        }
        async Task VerifyHistoryAsync(AiKnowledgeIndexTaskBinding b)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var parameters = await services.GetRequiredService<AiKnowledgeReadAccess>().ParametersAsync(session.UserId, cancellation);
            parameters["KnowledgeBaseId"] = b.KnowledgeBaseId; parameters["DocumentId"] = b.DocumentId; parameters["VersionId"] = b.VersionId;
            var source = await services.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(
                AiKnowledgeIndexStatusSql.For(provider, false), parameters, cancellation); Assert.IsNotNull(source);
            using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/chat/sessions");
            create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            create.Content = JsonContent.Create(new CreateAiChatSessionRequest(source.ModelConfigId!.Value, "原生来源历史"));
            using var created = await client.SendAsync(create, cancellation); await Require(created, HttpStatusCode.Created);
            var chat = await created.Content.ReadFromJsonAsync<AiChatSessionResponse>(cancellation); Assert.IsNotNull(chat);
            // 模型地址漂移使原索引的收费请求指纹证明失效；公开 Ask 必须先拒绝来源并释放槽位。
            var destination = await db.QuerySingleAsync<string>("SELECT EndpointBaseUrl FROM fn_ai_model_config WHERE Id=@Id", new { Id = source.ModelConfigId });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id",
                new { Id = source.ModelConfigId, Endpoint = "http://127.0.0.1:9/v1" });
            try
            {
                using var ask = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask");
                ask.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                ask.Content = JsonContent.Create(new AskAiKnowledgeRequest([b.KnowledgeBaseId], source.ModelConfigId!.Value, "原生来源问题"));
                using var blocked = await client.SendAsync(ask, cancellation); await Require(blocked, HttpStatusCode.Conflict);
                Assert.AreEqual("ai.knowledge.answer_unavailable", (await blocked.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("code").GetString());
                Assert.IsFalse((await blocked.Content.ReadAsStringAsync(cancellation)).Contains("127.0.0.1", StringComparison.Ordinal));
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_chat_session WHERE Id=@Id AND IsGenerating=1", new { Id = chat.Id }));
                Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_chat_message WHERE SessionId=@Id AND IsKnowledgeAnswer=1 AND StatusKey='failed' AND Content='' AND KnowledgeLineage IS NULL", new { Id = chat.Id }));
            }
            finally
            {
                await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id", new { Id = source.ModelConfigId, Endpoint = destination });
            }
            var messageId = Guid.CreateVersion7(); var citationId = Guid.CreateVersion7();
            var lineage = new AiKnowledgeAnswerLineage(docx ? 2 : 1, messageId, chat.Id, session.UserId, null,
                [new(source.KnowledgeBaseId, source.DocumentId, source.VersionId, source.DocumentVersion, source.KnowledgeBaseVersion,
                    source.FileId, source.ContentHash, source.TaskId!.Value, source.ActiveReceiptHash!, source.ActiveManifestHash!, source.ActivePlanHash!)],
                [new(citationId, source.DocumentId, source.VersionId, 0, docx ? null : 1, 0, 1) { DocxLocation = docx ? new(0, 1, null, null, null) : null }]);
            var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
            var ids = new[] { messageId, Guid.CreateVersion7(), Guid.CreateVersion7() };
            for (var i = 0; i < ids.Length; i++)
                await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) VALUES (@Id,@SessionId,'assistant',@Content,'completed',@Now,@Marked,@Lineage)",
                    new { Id = ids[i], SessionId = chat.Id, Content = i == 0 ? "原生可读知识答案" : "禁止泄露的损坏正文", Now = DateTimeOffset.UtcNow,
                        Marked = true, Lineage = i == 0 ? json : i == 1 ? null : "{}" });
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) VALUES (@Id,@SessionId,'assistant',@Content,'completed',@Now,0,'{}')",
                new { Id = Guid.CreateVersion7(), SessionId = chat.Id, Content = "禁止插入的不一致正文", Now = DateTimeOffset.UtcNow }));
            async Task<AiChatSessionResponse> ReadHistory()
            {
                using var response = await Send(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chat.Id}"); await Require(response, HttpStatusCode.OK);
                var value = await response.Content.ReadFromJsonAsync<AiChatSessionResponse>(cancellation); Assert.IsNotNull(value); return value;
            }
            var readable = await ReadHistory(); var answer = readable.Messages.Single(message => message.Id == messageId);
            Assert.AreEqual("原生可读知识答案", answer.Content); Assert.IsNull(answer.KnowledgeUnavailableCode);
            Assert.HasCount(1, answer.Citations); Assert.AreEqual(citationId, answer.Citations[0].Id); Assert.AreEqual(docx ? null : 1, answer.Citations[0].PageNumber);
            Assert.AreEqual(docx ? new AiKnowledgeDocxLocationResponse(0, 1, null, null, null) : null, answer.Citations[0].DocxLocation);
            Assert.IsTrue(answer.IsKnowledgeAnswer);
            async Task<HttpResponseMessage> FeedbackSend(HttpMethod method, string path, HttpContent? content)
            {
                using var request = new HttpRequestMessage(method, path) { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return await client.SendAsync(request, cancellation);
            }
            var feedback = await AiKnowledgeAnswerFeedbackAssertions.HttpAsync(FeedbackSend, chat.Id, answer);
            await NativeFeedbackReviewAssertions.VerifyAsync(db, FeedbackSend, source.ModelConfigId.Value, lineage, feedback);
            foreach (var id in ids.Skip(1))
            { var hidden = readable.Messages.Single(message => message.Id == id); Assert.AreEqual("", hidden.Content);
                Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, hidden.KnowledgeUnavailableCode); Assert.HasCount(0, hidden.Citations); }
            var receipt = await db.QuerySingleAsync<string>("SELECT ReceiptJson FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = b.DocumentId });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET ReceiptJson='[]' WHERE DocumentId=@Id", new { Id = b.DocumentId });
            try
            { var hidden = (await ReadHistory()).Messages.Single(message => message.Id == messageId); Assert.AreEqual("", hidden.Content);
                Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, hidden.KnowledgeUnavailableCode); Assert.HasCount(0, hidden.Citations);
                var feedbackPath = $"/api/v1/ai/chat/sessions/{chat.Id}/messages/{messageId}/feedback";
                using var payload = await FeedbackSend(HttpMethod.Get, feedbackPath, null); await Require(payload, HttpStatusCode.NotFound);
                using var state = await FeedbackSend(HttpMethod.Get, feedbackPath + "/state", null); await Require(state, HttpStatusCode.OK);
                var clear = await state.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackStateResponse>(cancellation); Assert.IsNotNull(clear); Assert.AreEqual(feedback.Version, clear.Version);
                using var withdrawn = await FeedbackSend(HttpMethod.Delete, feedbackPath + $"?version={clear.Version}", null); await Require(withdrawn, HttpStatusCode.OK);
            }
            finally { await db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET ReceiptJson=@Receipt WHERE DocumentId=@Id", new { Id = b.DocumentId, Receipt = receipt }); }
        }
        async Task<Guid> Enqueue(AiKnowledgeIndexTaskBinding b)
        {
            using var response = await Send(HttpMethod.Post, PathFor(b)); await Require(response, HttpStatusCode.Accepted);
            var queued = await response.Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>(cancellation); Assert.IsNotNull(queued);
            // 在产生任何费用前终止首代，再经真实 Native HTTP 创建独立代次；后续完整证明和恢复都绑定新任务。
            var previous = queued.TaskId;
            var previousSession = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = previous });
            Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed',FailureCode='ai.knowledge.index_unavailable' WHERE Id=@Id AND StateKey='queued'", new { Id = previous }));
            var rebuild = new AiKnowledgeIndexRebuildRequest(Guid.CreateVersion7(), previous, b.DocumentVersion, b.KnowledgeBaseVersion, true);
            using (var denied = await Send(HttpMethod.Post, PathFor(b) + "/rebuild", JsonContent.Create(rebuild with { ConfirmAdditionalCost = false })))
                await Require(denied, HttpStatusCode.BadRequest);
            using (var unknown = await Send(HttpMethod.Post, PathFor(b) + "/rebuild", new StringContent(
                JsonSerializer.Serialize(rebuild, JsonSerializerOptions.Web).TrimEnd('}') + ",\"tenantId\":null}", System.Text.Encoding.UTF8, "application/json")))
                await Require(unknown, HttpStatusCode.BadRequest);
            using (var accepted = await Send(HttpMethod.Post, PathFor(b) + "/rebuild", JsonContent.Create(rebuild)))
            {
                await Require(accepted, HttpStatusCode.Accepted);
                queued = await accepted.Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>(cancellation); Assert.IsNotNull(queued);
            }
            Assert.AreNotEqual(previous, queued.TaskId); Assert.AreEqual(b.VersionId, queued.VersionId);
            using (var replay = await Send(HttpMethod.Post, PathFor(b) + "/rebuild", JsonContent.Create(rebuild)))
            {
                await Require(replay, HttpStatusCode.Accepted);
                Assert.AreEqual(queued, await replay.Content.ReadFromJsonAsync<AiKnowledgeIndexEnqueueResponse>(cancellation));
            }
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE Id=@Id AND Generation=1 AND RebuildPreviousTaskId=@Previous AND RebuildRequestId=@Request", new { Id = queued.TaskId, Previous = previous, Request = rebuild.ClientRequestId }));
            Assert.AreEqual(2L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE VersionId=@Id", new { Id = b.VersionId }));
            Assert.AreEqual(previousSession, await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = previous }));
            Assert.AreEqual("failed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id AND Generation=0", new { Id = previous }));
            admittedTasks.Add(b.VersionId, queued.TaskId);
            if (!nativeWorker)
            {
                // 原生读取到失效冻结身份时，只建议独立新代次；对账不接管、续租或派发旧任务。
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_session SET ActorUserId=@Actor WHERE IndexTaskId=@TaskId", new { Actor = Guid.CreateVersion7(), TaskId = queued.TaskId });
                try
                {
                    using var reconciliation = await Send(HttpMethod.Get, PathFor(b) + "/reconciliation"); await Require(reconciliation, HttpStatusCode.OK);
                    var checkedIndex = await reconciliation.Content.ReadFromJsonAsync<AiKnowledgeIndexReconciliationResponse>(cancellation);
                    Assert.IsNotNull(checkedIndex); Assert.AreEqual("failed", checkedIndex.State);
                    Assert.AreEqual("generation_unavailable", checkedIndex.Reason); Assert.AreEqual("rebuild", checkedIndex.RecoveryAction);
                }
                finally { await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_session SET ActorUserId=@Actor WHERE IndexTaskId=@TaskId", new { Actor = b.OwnerUserId, TaskId = queued.TaskId }); }
            }
            return queued.TaskId;
        }
        async Task Recover(AiKnowledgeIndexTaskBinding b)
        {
            // 人工重试沿原会话冻结绑定运行，保留已结算清单和向量；Worker 不接触假 Node/CLI 路径。
            var task = admittedTasks[b.VersionId];
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='failed',FailureCode='ai.knowledge.index_unavailable' WHERE Id=@Id", new { Id = task });
            using var retry = await Send(HttpMethod.Post, PathFor(b) + $"/{task}/retry"); await Require(retry, HttpStatusCode.Accepted);
            if (!nativeWorker)
            {
                // API 门禁只依赖 API 原生产物；独立 Worker 门禁另用真实原生进程验证恢复。
                await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
                services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                var coordinator = new AiKnowledgeIndexWorkerCoordinator(services.GetRequiredService<IQueryExecutor>(),
                    services.GetRequiredService<IDataTransactionState>(), services.GetRequiredService<IServiceScopeFactory>(), clock,
                    services.GetRequiredService<IOptions<DatabaseOptions>>(), Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = true, BatchSize = 1 }),
                    NullLogger<AiKnowledgeIndexWorkerCoordinator>.Instance, Options.Create(new AiKnowledgePdfRuntimeOptions { Enabled = !docx }),
                    Options.Create(new AiKnowledgeDocxRuntimeOptions { Enabled = docx }));
                Assert.AreEqual(1, await coordinator.ProcessPendingAsync(cancellation)); await ReadReady(b); return;
            }
            Assert.IsTrue(NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason), reason);
            Assert.IsNotNull(artifact); var settings = Settings(artifact.ExecutablePath);
            if (docx)
            {
                // 统一档消费已结算完整计划；若回退为重读或派发，假程序路径必须使验收失败。
                settings["FullNet:Ai:KnowledgeDocuments:Enabled"] = "true";
                settings["FullNet:Ai:KnowledgeDocuments:NodeExecutablePath"] = artifact.ExecutablePath;
                settings["FullNet:Ai:KnowledgeDocuments:CliScriptPath"] = artifact.ExecutablePath;
                settings["FullNet:Ai:KnowledgeDocuments:ImageId"] = "sha256:" + new string('a', 64);
            }
            settings["FullNet__Ai__KnowledgeIndexWorker__Enabled"] = "true";
            settings["FullNet__Ai__KnowledgeIndexWorker__BatchSize"] = "1";
            settings["FullNet__Ai__KnowledgeIndexWorker__PollMilliseconds"] = "250";
            await using var worker = await NativeWorkerProcessHost.StartAsync(artifact, provider, connectionString,
                NativeAotTestTimeouts.ProcessStartup, cancellation, additionalSettings: settings);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(2));
            while (true)
            {
                var state = await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task });
                if (state == "completed") break;
                Assert.AreNotEqual("failed", state, await File.ReadAllTextAsync(worker.LogFilePath, cancellation));
                deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(250, deadline.Token);
            }
            await ReadReady(b);
        }
        if (docx) await AiKnowledgeIndexDocumentPersistenceAssertions.VerifyAsync(factory, clock, docxControls, new(session, Enqueue, ReadReady, Recover));
        else await AiKnowledgeIndexPdfVectorAssertions.VerifyAsync(factory, clock, controls, new(session, Enqueue, ReadReady, Recover));
    }
}
