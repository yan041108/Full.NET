using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>正式人工修订由原生Worker生成并恢复；控制器只准备身份、HTTP请求和独享数据库故障。</summary>
[TestClass, DoNotParallelize]
public sealed class NativeWorkerCorrectionGenerationE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_worker_generates_human_revision_and_restores_paid_vectors_without_dispatch(DatabaseProvider provider)
    {
        Assert.IsTrue(NativeApiArtifactLocator.TryResolve(out var apiArtifact, out var apiReason), apiReason);
        Assert.IsTrue(NativeWorkerArtifactLocator.TryResolve(out var workerArtifact, out var workerReason), workerReason);
        Assert.IsNotNull(apiArtifact); Assert.IsNotNull(workerArtifact);
        var node = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_NODE");
        var cli = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_CLI");
        var image = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_IMAGE");
        Assert.IsTrue(File.Exists(node) && File.Exists(cli) && image?.StartsWith("sha256:", StringComparison.Ordinal) == true);
        await using var model = await NativeKnowledgeModelServer.StartAsync();
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "native-correction-keys", Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "native-correction-files", Guid.CreateVersion7().ToString("N")),
            ["FullNet:Ai:KnowledgeDocuments:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeDocuments:NodeExecutablePath"] = node,
            ["FullNet:Ai:KnowledgeDocuments:CliScriptPath"] = cli,
            ["FullNet:Ai:KnowledgeDocuments:ImageId"] = image,
            ["FullNet:Ai:KnowledgeIndexRebuild:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "false",
            ["FullNet:Ai:KnowledgeIndexWorker:CorrectionPreparationEnabled"] = "true",
            ["FullNet:Ai:KnowledgeIndexWorker:BatchSize"] = "1",
            ["FullNet:Ai:KnowledgeIndexWorker:PollMilliseconds"] = "250"
        };
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings);
        await factory.InitializeAsync();
        var username = "native-correction-" + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(username,
            [AiModelPermissions.Read, AiModelPermissions.Create, AiKnowledgePermissions.Read, AiKnowledgePermissions.Create,
                AiKnowledgePermissions.PolicyUpdate, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create,
                AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
                AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRetry,
                AiKnowledgeCorrectionPermissions.Prepare, AiKnowledgeCorrectionPermissions.Read, AiKnowledgeCorrectionPermissions.Edit],
            password: FullNetApiFactory.TestPassword);
        await using var api = await NativeApiProcessHost.StartAsync(apiArtifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username));
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        var embedding = await CreateModel("text-embedding-3-small");
        var generation = await CreateModel("gpt-4.1-mini");
        // 独享夹具价格是操作前置数据，不预制任何预算、结算或成功状态。
        await db.ExecuteAsync("INSERT INTO fn_ai_model_price (Id,ModelConfigId,ProviderKey,ModelId,Currency,InputPerMillion,OutputPerMillion,CachedInputPerMillion,ValidFromUtc) VALUES (@Id,@Model,'openai_compatible','text-embedding-3-small','USD',2,4,1,@Since)",
            new { Id = Guid.CreateVersion7(), Model = embedding.Id, Since = DateTime.UtcNow.AddDays(-1) });
        var knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("原生人工结算恢复", null), HttpStatusCode.Created);
        var root = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, root + "/policy", new UpdateAiKnowledgePolicyRequest("internal", embedding.Id, embedding.Version, generation.Id, generation.Version, knowledge.Version));
        var document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Post, root + "/documents", new CreateAiKnowledgeDocumentRequest("人工修订", null), HttpStatusCode.Created);
        var path = root + "/documents/" + document.Id;
        const string original = "原始来源不得再次送模。";
        const string corrected = "已人工确认的最终知识。";
        using var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(original)); bytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var upload = await client.PostAsync(path + $"/versions?version={document.Version}&requestKey={Guid.CreateVersion7()}&fileName=source.txt", bytes);
        await Require(upload, HttpStatusCode.Accepted);
        var imported = (await upload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
        Assert.AreEqual("completed", imported.State);
        document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path);
        var correction = path + $"/versions/{imported.VersionId}/corrections";
        var indexing = path + $"/versions/{imported.VersionId}/index";
        var preparation = await Send<AiKnowledgeCorrectionPreparationResponse>(HttpMethod.Post, correction + "/prepare", new AiKnowledgeCorrectionPrepareRequest(Guid.CreateVersion7(), document.Version), HttpStatusCode.Accepted);
        await using (var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection, NativeAotTestTimeouts.ProcessStartup, additionalSettings: settings))
        {
            await WaitFor(async () =>
            {
                var state = await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { preparation.Id });
                Assert.IsFalse(state is "failed" or "cancelled", "原生来源准备失败：" + state + "; " + await File.ReadAllTextAsync(worker.LogFilePath));
                return state == "completed";
            });
            await Stop(worker);
        }
        Assert.AreEqual(0, model.EmbeddingInputs.Count, "原解析准备不得派发付费模型。");
        var current = await Send<AiKnowledgeCorrectionWorkspaceResponse>(HttpMethod.Get, correction + "/workspace?block=0");
        Assert.AreEqual(original, current.Block.Parts[0].OriginalText);
        await Send<AiKnowledgeCorrectionRevisionResponse>(HttpMethod.Post, correction + "/revisions", new AiKnowledgeCorrectionEditRequest(Guid.CreateVersion7(),
            new(current.DocumentVersion, current.SourceId, current.RevisionId, current.HeadVersion, current.SourceHash, current.PayloadHash),
            "原生恢复修订", new("replace", current.Block.Id, current.Block.Parts[0].Id, corrected, null, null)));
        current = await Send<AiKnowledgeCorrectionWorkspaceResponse>(HttpMethod.Get, correction + "/workspace?block=0");
        var revision = current.RevisionId ?? throw new AssertFailedException("正式编辑必须产生人工修订。");
        Guid taskId;
        var fault = false;
        try
        {
            await ActivationFault(true); fault = true;
            taskId = (await Send<JsonElement>(HttpMethod.Post, indexing, expected: HttpStatusCode.Accepted)).GetProperty("taskId").GetGuid();
            var identity = await db.QuerySingleAsync<(Guid Source, Guid Revision, int Head)>("SELECT CorrectionSourceId,CorrectionRevisionId,CorrectionHeadVersion FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = taskId });
            Assert.AreEqual(current.SourceId, identity.Source); Assert.AreEqual(revision, identity.Revision); Assert.AreEqual(current.HeadVersion, identity.Head);
            await RunGeneration("failed");
            Assert.AreEqual(1, model.EmbeddingInputs.Count, "短文本只能产生一次真实付费派发。");
            var sent = string.Join('\n', model.EmbeddingInputs.SelectMany(row => row));
            StringAssert.Contains(sent, corrected); Assert.IsFalse(sent.Contains(original, StringComparison.Ordinal));
            var ledger = await Ledger();
            Assert.AreEqual(1, ledger.Length); Assert.AreEqual(1L, ledger[0].Tokens); Assert.AreEqual("succeeded", ledger[0].Outcome); Assert.AreEqual("known", ledger[0].Usage); Assert.IsFalse(string.IsNullOrWhiteSpace(ledger[0].Settled));
            Assert.AreEqual(0.000002m, ledger[0].Cost, "真实结算必须采用明确测试价格，不能以未知金额代替非零费用证明。");
            var payload = await db.QuerySingleAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@Id", new { Id = taskId });
            Assert.IsTrue(payload.Length > 0);
            Assert.AreEqual("knowledge-correction-vector-v1", await db.QuerySingleAsync<string>("SELECT vector.FormatKey FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@Id", new { Id = taskId }));
            Assert.AreEqual("knowledge-correction-manifest-v1", await db.QuerySingleAsync<string>("SELECT FormatKey FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = taskId }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE IndexTaskId=@Id", new { Id = taskId }));
            await ActivationFault(false); fault = false;
            using var retry = await client.PostAsync(indexing + $"/{taskId}/retry", null); await Require(retry, HttpStatusCode.Accepted);
            model.RejectEmbeddings = true;
            try { await RunGeneration("ready"); } finally { model.RejectEmbeddings = false; }
            Assert.AreEqual(1, model.EmbeddingInputs.Count, "恢复禁止重新派发，受控模型在拒绝之前也会计数。");
            CollectionAssert.AreEqual(ledger, await Ledger(), "恢复不得改写已结算费用、Token或结算时间。");
            CollectionAssert.AreEqual(payload, await db.QuerySingleAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@Id", new { Id = taskId }));
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = taskId }));
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE IndexTaskId=@Id", new { Id = taskId }));
            Console.WriteLine(JsonSerializer.Serialize(new { nativeCorrection = true, provider = provider.ToString(), taskId, source = current.SourceId, revision = current.RevisionId, current.HeadVersion, calls = model.EmbeddingInputs.Count, ledger[0].Tokens, ledger[0].Cost }));
        }
        finally { if (fault) await ActivationFault(false); }
        Assert.AreEqual(0, model.ChatCalls);
        await api.StopGracefullyAsync(); api.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, api.ExitCode);

        async Task RunGeneration(string expected)
        {
            var workerSettings = new Dictionary<string, string?>(settings) { ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "true" };
            await using var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection, NativeAotTestTimeouts.ProcessStartup, additionalSettings: workerSettings);
            await WaitFor(async () =>
            {
                var status = await Send<AiKnowledgeIndexStatusResponse>(HttpMethod.Get, indexing);
                if (status.State == "failed" && expected != "failed") Assert.Fail("原生恢复失败：" + status.FailureCode + "; " + await File.ReadAllTextAsync(worker.LogFilePath));
                return status.State == expected;
            });
            await Stop(worker);
        }
        async Task<(Guid Id, long Tokens, decimal? Cost, string Outcome, string Usage, string? Settled)[]> Ledger()
        {
            var settled = provider == DatabaseProvider.SqlServer ? "CONVERT(varchar(64),budget.SettledAtUtc,127)" : "DATE_FORMAT(budget.SettledAtUtc,'%Y-%m-%dT%H:%i:%s.%f')";
            return (await db.QueryAsync<(Guid, long, decimal?, string, string, string?)>("SELECT budget.Id,budget.ChargedTokens,budget.ChargedCost,budget.Outcome,budget.UsageStatus," + settled + " FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@Id ORDER BY batch.BatchOrdinal", new { Id = taskId })).ToArray();
        }
        async Task ActivationFault(bool install)
        {
            const string name = "fullnet_test_native_correction_activation";
            if (!install) { await db.ExecuteAsync($"DROP TRIGGER {name}"); return; }
            // 故障只在本例独享库中截断最终激活，不预制任何成功、所有权或结算状态。
            var id = document.Id.ToString("D");
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? $"CREATE TRIGGER {name} ON fn_ai_knowledge_active_generation AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE DocumentId='{id}') THROW 51000, 'native correction activation fault', 1; END"
                : $"CREATE TRIGGER {name} BEFORE INSERT ON fn_ai_knowledge_active_generation FOR EACH ROW BEGIN IF NEW.DocumentId=UNHEX(REPLACE('{id}','-','')) THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='native correction activation fault'; END IF; END");
        }
        async Task<AiModelConfigResponse> CreateModel(string name) => await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, name, "openai_compatible", "https://api.openai.com/v1", name, "native-test-placeholder", null, false, true), HttpStatusCode.Created);
        async Task<T> Send<T>(HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        { using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }; using var response = await client.SendAsync(request); await Require(response, expected); return (await response.Content.ReadFromJsonAsync<T>())!; }
        static async Task Require(HttpResponseMessage response, HttpStatusCode expected) => Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        static async Task WaitFor(Func<Task<bool>> condition)
        { using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3)); while (!await condition()) await Task.Delay(200, deadline.Token); }
        static async Task Stop(NativeWorkerProcessHost worker)
        { await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30)); worker.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, worker.ExitCode); }
    }
}
