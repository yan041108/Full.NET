using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native API 发布来源确认，由真实 Native Worker 消费 Host、Tenant、后续 Host 信封；不派发模型。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiKnowledgeIncrementalIndexE2ETests
{
    [TestMethod]
    public Task SqlServer_native_api_and_worker_confirm_incremental_sources_without_dispatch() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_native_api_and_worker_confirm_incremental_sources_without_dispatch() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        if (!NativeApiArtifactLocator.TryResolve(out var api, out var apiReason)) Assert.Inconclusive(apiReason ?? "Native API artifact unavailable.");
        if (!NativeWorkerArtifactLocator.TryResolve(out var workerArtifact, out var workerReason)) Assert.Inconclusive(workerReason ?? "Native Worker artifact unavailable.");
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connection);
        await using var host = await NativeApiProcessHost.StartAsync(api, provider, connection, new Dictionary<string, string?>(), NativeAotTestTimeouts.ProcessStartup, CancellationToken.None);
        await using var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection, NativeAotTestTimeouts.ProcessStartup,
            additionalSettings: new Dictionary<string, string?> { ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "false" });
        using var client = host.CreateClient();
        using var configuration = new FullNetApiFactory(provider, connection);
        await using var db = AiOperationBudgetAssertions.Connection(configuration);
        var token = await NativeApiE2EAssertions.LoginAsync(client, host.LogFilePath);
        var expectedEvents = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType });
        foreach (var phase in new[] { "host", "tenant", "host-after-tenant" })
        {
            if (phase == "tenant") token = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, token);
            if (phase == "host-after-tenant")
            {
                client.DefaultRequestHeaders.Authorization = null;
                token = await NativeApiE2EAssertions.LoginAsync(client, host.LogFilePath);
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var createdBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("原生来源 " + phase, null));
            Assert.AreEqual(HttpStatusCode.Created, createdBase.StatusCode, await createdBase.Content.ReadAsStringAsync());
            var knowledge = (await createdBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents";
            using var createdDocument = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("来源替换", null));
            Assert.AreEqual(HttpStatusCode.Created, createdDocument.StatusCode, await createdDocument.Content.ReadAsStringAsync());
            var document = (await createdDocument.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
            path += $"/{document.Id}";
            var versionId = Guid.Empty;
            for (var version = 1; version <= 2; version++)
            {
                var retry = Guid.CreateVersion7();
                var uploadPath = path + $"/versions?version={version}&requestKey={retry}&fileName=source.txt";
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var uploaded = await client.PostAsync(uploadPath, new StringContent("原生确认来源 " + version, Encoding.UTF8, "text/plain"));
                    Assert.AreEqual(HttpStatusCode.Accepted, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync());
                    var result = (await uploaded.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
                    Assert.AreEqual("completed", result.State);
                    if (attempt == 1) Assert.AreEqual(versionId, result.VersionId);
                    versionId = result.VersionId;
                }
                expectedEvents++;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (true)
            {
                var latest = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT VersionId FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id });
                var processed = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type AND ProcessedAtUtc IS NOT NULL AND DeadLetteredAtUtc IS NULL", new { Type = AiKnowledgeSourceConfirmed.EventType });
                var dead = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type AND DeadLetteredAtUtc IS NOT NULL", new { Type = AiKnowledgeSourceConfirmed.EventType });
                if (dead != 0) Assert.Fail(await File.ReadAllTextAsync(worker.LogFilePath));
                if (latest == versionId && processed == expectedEvents) break;
                await Task.Delay(100, deadline.Token);
            }
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id }));
            // 事件已经确认消费后移除派生投影，验证同一原生产物可恢复历史来源并准确报告幂等计数。
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id });
            var recoveryPath = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/index-changes/recover";
            using var recovered = await client.PostAsJsonAsync(recoveryPath, new AiKnowledgeIncrementalRecoveryRequest());
            Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync());
            var receipt = (await recovered.Content.ReadFromJsonAsync<AiKnowledgeIncrementalRecoveryResponse>())!;
            Assert.AreEqual(1, receipt.Scanned); Assert.AreEqual(1, receipt.Updated); Assert.IsNull(receipt.NextDocumentId);
            using var repeated = await client.PostAsJsonAsync(recoveryPath, new AiKnowledgeIncrementalRecoveryRequest());
            Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
            Assert.AreEqual(0, (await repeated.Content.ReadFromJsonAsync<AiKnowledgeIncrementalRecoveryResponse>())!.Updated);
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@Id", new { Id = document.Id }));
            var versions = (await client.GetFromJsonAsync<PagedResult<AiKnowledgeVersionResponse>>(path + "/versions"))!;
            Assert.AreEqual("awaiting_index_confirmation", versions.Items.Single(item => item.Id == versionId).ProcessingStatus);
            Assert.AreEqual(expectedEvents, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType }));
        }
        await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        worker.AssertNoFatalMarkersInLogs();
        await host.StopGracefullyAsync(CancellationToken.None);
        host.AssertNoFatalMarkersInLogs();
    }
}
