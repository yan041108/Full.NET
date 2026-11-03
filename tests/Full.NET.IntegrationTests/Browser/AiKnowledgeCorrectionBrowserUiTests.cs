using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.Browser;

/// <summary>Linux 原生 API/真实 MySQL 与生产 Worker 的浏览器桥接；Windows 仅运行标准 Vue/Edge，不加载 Ai 程序集。</summary>
[TestClass]
[TestCategory("BrowserUi")]
[DoNotParallelize]
public sealed class AiKnowledgeCorrectionBrowserUiTests
{
    [TestMethod]
    public async Task MySql_native_workbench_browser_bridge_preserves_real_http_state()
    {
        var bridge = Environment.GetEnvironmentVariable("FULLNET_K02_BROWSER_BRIDGE");
        Assert.IsFalse(string.IsNullOrWhiteSpace(bridge), "浏览器专项必须显式提供系统临时桥接目录。");
        var origin = Environment.GetEnvironmentVariable("FULLNET_K02_BROWSER_ORIGIN") ?? "http://localhost:25173";
        var port = int.Parse(Environment.GetEnvironmentVariable("FULLNET_K02_BROWSER_PORT") ?? "64159", System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(bridge!);
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var connection = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(bridge!, "keys"),
            ["Files:Local:RootPath"] = Path.Combine(bridge!, "files"),
            ["Identity:AllowedOrigins:1"] = origin,
            ["Tenancy:HostDomains:1"] = "127.0.0.1"
        };
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, connection, settingsOverrides: settings); await factory.InitializeAsync();
        var username = "correction-browser-" + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(username,
            // 正式目录的领域和 AI 模块父节点分别要求仪表盘与模型只读权限，不能绕过真实导航投影。
            ["identity.navigation.read", "platform.dashboard.read", AiModelPermissions.Read, AiKnowledgePermissions.Read, AiKnowledgePermissions.Create,
                AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Upload,
                AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeDocumentPermissions.Delete,
                AiKnowledgeCorrectionPermissions.Read, AiKnowledgeCorrectionPermissions.Prepare, AiKnowledgeCorrectionPermissions.Edit,
                AiKnowledgeCorrectionPermissions.Restore], password: FullNetApiFactory.TestPassword);
        await using var api = await NativeApiProcessHost.StartAsync(artifact, DatabaseProvider.MySql, connection, settings,
            NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri($"http://0.0.0.0:{port}/"));
        using var client = api.CreateClient(); var access = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username);
        client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        var knowledgeName = "真实校对浏览器 " + Guid.CreateVersion7().ToString("N")[..8];
        var knowledge = await Post<AiKnowledgeBaseResponse>("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest(knowledgeName, null), HttpStatusCode.Created);
        var documents = new List<object>(); const string original = "浏览器真实原文😀，保留实际位置。";
        foreach (var width in new[] { 1440, 390 })
        {
            var title = "真实校对 " + width;
            var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents";
            var document = await Post<AiKnowledgeDocumentResponse>(path, new CreateAiKnowledgeDocumentRequest(title, null), HttpStatusCode.Created);
            path += "/" + document.Id;
            using var upload = await client.PostAsync(path + $"/versions?version={document.Version}&requestKey={Guid.CreateVersion7()}&fileName=source.txt", new StringContent(original, Encoding.UTF8, "text/plain"));
            Assert.AreEqual(HttpStatusCode.Accepted, upload.StatusCode, await upload.Content.ReadAsStringAsync());
            var version = (await upload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.VersionId;
            documents.Add(new { width, title, documentId = document.Id, versionId = version });
        }
        using var worker = await IntegrationWorkerHostFactory.BuildAsync(DatabaseProvider.MySql, connection, new Dictionary<string, string?>
        {
            [$"{AiKnowledgeIndexWorkerOptions.SectionName}:CorrectionPreparationEnabled"] = "true",
            [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"] = "250"
        }, "CorrectionBrowserWorker", nameof(AiKnowledgeIndexHostedProcessor), settings["Files:Local:RootPath"]);
        await worker.StartAsync();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(bridge!, "ready.json"), JsonSerializer.Serialize(new
            {
                username, password = FullNetApiFactory.TestPassword, knowledgeBaseId = knowledge.Id, knowledgeName, original, documents, apiLog = api.LogFilePath
            }));
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var completion = Path.Combine(bridge!, "complete.json");
            while (!File.Exists(completion)) await Task.Delay(250, deadline.Token);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(completion, deadline.Token));
            Assert.IsTrue(result.RootElement.GetProperty("passed").GetBoolean(), "真实栈 Playwright 未通过，保留其原始报告。");
        }
        finally { await worker.StopAsync(); }
        await api.StopGracefullyAsync(); api.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, api.ExitCode);
        Console.WriteLine($"Native correction browser API log: {api.LogFilePath}");

        async Task<T> Post<T>(string path, object body, HttpStatusCode expected)
        {
            using var response = await client.PostAsync(path, JsonContent.Create(body, body.GetType()));
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
    }
}
