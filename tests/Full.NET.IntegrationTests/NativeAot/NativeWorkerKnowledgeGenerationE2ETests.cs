using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>双库真实文件从原生上传、隔离解析到首次模型派发、记账、完整激活、引用及确定性恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerKnowledgeGenerationE2ETests
{
    [TestMethod]
    public Task SqlServer_native_worker_generates_and_recovers_real_knowledge() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_native_worker_generates_and_recovers_real_knowledge() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        if (!NativeApiArtifactLocator.TryResolve(out var apiArtifact, out var apiReason)) Assert.Inconclusive(apiReason ?? "Native API artifact unavailable.");
        if (!NativeWorkerArtifactLocator.TryResolve(out var workerArtifact, out var workerReason)) Assert.Inconclusive(workerReason ?? "Native Worker artifact unavailable.");
        Assert.IsNotNull(apiArtifact); Assert.IsNotNull(workerArtifact);
        var files = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_FIXTURES");
        var node = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_NODE");
        var cli = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_CLI");
        var image = Environment.GetEnvironmentVariable("FULLNET_NATIVE_KNOWLEDGE_IMAGE");
        Assert.IsTrue(Directory.Exists(files) && File.Exists(node) && File.Exists(cli), "原生完整生成门禁必须提供真实解析工具与原始样本。");
        Assert.IsTrue(image?.StartsWith("sha256:", StringComparison.Ordinal) == true && image.Length == 71);
        await using var modelServer = await NativeKnowledgeModelServer.StartAsync();
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-native-generation-keys", Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-native-generation-files", Guid.CreateVersion7().ToString("N")),
            ["FullNet:Ai:KnowledgeDocuments:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeDocuments:NodeExecutablePath"] = node,
            ["FullNet:Ai:KnowledgeDocuments:CliScriptPath"] = cli,
            ["FullNet:Ai:KnowledgeDocuments:ImageId"] = image,
            ["FullNet:Ai:KnowledgeIndexRebuild:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeIndexWorker:BatchSize"] = "1",
            ["FullNet:Ai:KnowledgeIndexWorker:PollMilliseconds"] = "250",
        };
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings);
        await factory.InitializeAsync();
        var username = $"native-generation-{Guid.CreateVersion7():N}";
        string[] permissions = [AiModelPermissions.Read, AiModelPermissions.Create, AiKnowledgePermissions.Read, AiKnowledgePermissions.Create,
            AiKnowledgePermissions.PolicyUpdate, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
            AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRetry,
            AiChatPermissions.Read, AiChatPermissions.Create, AiChatPermissions.Send];
        await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
        await using var api = await NativeApiProcessHost.StartAsync(apiArtifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        using var client = api.CreateClient();
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)) };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var login = await client.SendAsync(loginRequest); await Require(login, HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        var embedding = await CreateModel("text-embedding-3-small"); var generation = await CreateModel("gpt-4.1-mini");
        var examples = new (string Name, string Mime, byte[] Bytes, bool Recover)[]
        {
            ("source.txt", "text/plain", Encoding.UTF8.GetBytes("知识库解析测试。原生完整生成验证。"), true),
            ("source.csv", "text/csv", Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Range(0, 18).Select(i => $"知识库解析测试原生完整生成验证第{i}行"))), true),
            ("source.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", await File.ReadAllBytesAsync(Path.Combine(files!, "source.docx")), false),
            ("scanned.pdf", "application/pdf", await File.ReadAllBytesAsync(Path.Combine(files!, "scanned.pdf")), false),
            ("source.png", "image/png", await File.ReadAllBytesAsync(Path.Combine(files!, "source.png")), false),
        };
        foreach (var example in examples)
        {
            using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest(example.Name, null));
            await Require(created, HttpStatusCode.Created); var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var root = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
            using var policy = await client.PutAsJsonAsync(root + "/policy", new UpdateAiKnowledgePolicyRequest("internal", embedding.Id, embedding.Version, generation.Id, generation.Version, knowledge.Version));
            await Require(policy, HttpStatusCode.OK);
            using var documentResponse = await client.PostAsJsonAsync(root + "/documents", new CreateAiKnowledgeDocumentRequest(example.Name, null));
            await Require(documentResponse, HttpStatusCode.Created); var document = (await documentResponse.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
            var path = root + $"/documents/{document.Id}";
            using var bytes = new ByteArrayContent(example.Bytes); bytes.Headers.ContentType = new MediaTypeHeaderValue(example.Mime);
            using var upload = await client.PostAsync(path + $"/versions?version={document.Version}&requestKey={Guid.CreateVersion7()}&fileName={example.Name}", bytes);
            await Require(upload, HttpStatusCode.Accepted); var imported = (await upload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
            Assert.AreEqual("completed", imported.State);
            path += $"/versions/{imported.VersionId}/index";
            using var enqueue = await client.PostAsync(path, null); await Require(enqueue, HttpStatusCode.Accepted);
            var taskId = (await enqueue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Id = taskId }));
            var callsBefore = modelServer.EmbeddingInputs.Count;
            if (example.Recover) await ActivationFault(true);
            await RunWorker(settings, example.Recover ? "failed" : "ready");
            var callsAfter = modelServer.EmbeddingInputs.Count;
            Assert.IsTrue(callsAfter > callsBefore, "首次生成必须实际到达受控 HTTPS 模型，不能只种入已结算载荷。");
            Assert.IsTrue(modelServer.EmbeddingInputs.Skip(callsBefore).SelectMany(x => x).Any(x => x.Contains("知识库", StringComparison.Ordinal)), "模型必须收到真实解析正文。");
            var budgets = (await db.QueryAsync<(Guid Id, long ChargedTokens, string Outcome, string UsageStatus)>(
                "SELECT budget.Id,budget.ChargedTokens,budget.Outcome,budget.UsageStatus FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@Id ORDER BY batch.BatchOrdinal", new { Id = taskId })).ToArray();
            Assert.IsTrue(budgets.Length > 0); Assert.IsTrue(budgets.All(x => x.Outcome == "succeeded" && x.UsageStatus == "known" && x.ChargedTokens > 0));
            var vectors = (await db.QueryAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@Id ORDER BY batch.BatchOrdinal", new { Id = taskId })).ToArray();
            Assert.AreEqual(budgets.Length, vectors.Length);
            if (example.Name.EndsWith(".csv", StringComparison.Ordinal)) Assert.IsTrue(vectors.Length >= 2, "完整清单必须包含多个真实付费批次。");
            if (example.Recover)
            {
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE IndexTaskId=@Id", new { Id = taskId }));
                await ActivationFault(false);
                using var retry = await client.PostAsync(path + $"/{taskId}/retry", null); await Require(retry, HttpStatusCode.Accepted);
                // 恢复进程若再次解析将执行无效 CLI；模型则明确拒绝任何重新派发。
                var recoverySettings = new Dictionary<string, string?>(settings) { ["FullNet:Ai:KnowledgeDocuments:CliScriptPath"] = workerArtifact.ExecutablePath };
                modelServer.RejectEmbeddings = true;
                try { await RunWorker(recoverySettings, "ready"); }
                finally { modelServer.RejectEmbeddings = false; }
                Assert.AreEqual(callsAfter, modelServer.EmbeddingInputs.Count, "持久化代次恢复禁止重新派发。");
                CollectionAssert.AreEqual(budgets, (await db.QueryAsync<(Guid Id, long ChargedTokens, string Outcome, string UsageStatus)>(
                    "SELECT budget.Id,budget.ChargedTokens,budget.Outcome,budget.UsageStatus FROM fn_ai_operation_budget AS budget INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=budget.Id WHERE batch.IndexTaskId=@Id ORDER BY batch.BatchOrdinal", new { Id = taskId })).ToArray());
                var restored = (await db.QueryAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@Id ORDER BY batch.BatchOrdinal", new { Id = taskId })).ToArray();
                for (var i = 0; i < vectors.Length; i++) CollectionAssert.AreEqual(vectors[i], restored[i]);
                Console.WriteLine(JsonSerializer.Serialize(new { recovery = "persisted-generation", provider = provider.ToString(), format = example.Mime, callsBeforeRecovery = callsAfter, callsAfterRecovery = modelServer.EmbeddingInputs.Count, generated = true }));
            }
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE IndexTaskId=@Id", new { Id = taskId }));
            using var chatResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(generation.Id, "原生完整生成"));
            await Require(chatResponse, HttpStatusCode.Created); var chat = (await chatResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;
            using var ask = await client.PostAsJsonAsync($"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask", new AskAiKnowledgeRequest([knowledge.Id], embedding.Id, "知识库解析测试说明"));
            await Require(ask, HttpStatusCode.OK); var answer = (await ask.Content.ReadFromJsonAsync<AskAiKnowledgeResponse>())!;
            Assert.IsFalse(answer.Refused); Assert.AreEqual("原生完整生成验证", answer.Message.Content);
            Assert.IsTrue(answer.Message.Citations.Count > 0); Assert.AreEqual(imported.VersionId, answer.Message.Citations[0].VersionId);
            var lineage = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = answer.Message.Id });
            using var lineageJson = JsonDocument.Parse(lineage);
            Assert.AreEqual(3, lineageJson.RootElement.GetProperty("version").GetInt32());
            Console.WriteLine(JsonSerializer.Serialize(new { nativeGeneration = true, provider = provider.ToString(), format = example.Mime, batches = budgets.Length, chargedTokens = budgets.Sum(x => x.ChargedTokens), citations = answer.Message.Citations.Count }));

            async Task RunWorker(IReadOnlyDictionary<string, string?> configuration, string expected)
            {
                await using var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection, NativeAotTestTimeouts.ProcessStartup, additionalSettings: configuration);
                var timer = Stopwatch.StartNew(); string? state = null;
                while (timer.Elapsed < TimeSpan.FromMinutes(3))
                {
                    using var status = await client.GetAsync(path); await Require(status, HttpStatusCode.OK);
                    state = (await status.Content.ReadFromJsonAsync<AiKnowledgeIndexStatusResponse>())!.State;
                    if (state == expected || state == "failed") break;
                    await Task.Delay(200);
                }
                Assert.AreEqual(expected, state, await File.ReadAllTextAsync(worker.LogFilePath));
                await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30)); worker.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, worker.ExitCode);
            }
            async Task ActivationFault(bool install)
            {
                const string name = "fullnet_test_native_activation_fault";
                if (!install) { await db.ExecuteAsync($"DROP TRIGGER {name}"); return; }
                // 故障仅绑定本例临时数据库与文档；在费用和向量提交后中断原子激活，不能预制成功结果。
                var id = document.Id.ToString("D");
                var sql = provider == DatabaseProvider.SqlServer
                    ? $"CREATE TRIGGER {name} ON fn_ai_knowledge_active_generation AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE DocumentId='{id}') THROW 51000, 'native test activation fault', 1; END"
                    : $"CREATE TRIGGER {name} BEFORE INSERT ON fn_ai_knowledge_active_generation FOR EACH ROW BEGIN IF NEW.DocumentId=UNHEX(REPLACE('{id}','-','')) THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='native test activation fault'; END IF; END";
                await db.ExecuteAsync(sql);
            }
        }
        Assert.AreEqual(examples.Length, modelServer.ChatCalls);
        await api.StopGracefullyAsync(); api.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, api.ExitCode);

        async Task<AiModelConfigResponse> CreateModel(string model)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, model, "openai_compatible", "https://api.openai.com/v1", model, "native-test-placeholder", null, false, true));
            await Require(response, HttpStatusCode.Created); return (await response.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        }
        static async Task Require(HttpResponseMessage response, HttpStatusCode expected) => Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
