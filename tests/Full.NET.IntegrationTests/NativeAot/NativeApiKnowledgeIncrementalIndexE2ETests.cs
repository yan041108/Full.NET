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
        await using var host = await NativeApiProcessHost.StartAsync(api, provider, connection,
            new Dictionary<string, string?> { ["FullNet:Ai:KnowledgeIndexGrants:Enabled"] = "true" }, NativeAotTestTimeouts.ProcessStartup, CancellationToken.None);
        await using var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection, NativeAotTestTimeouts.ProcessStartup,
            additionalSettings: new Dictionary<string, string?>
            {
                ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "false",
                ["FullNet:Ai:KnowledgeIncrementalRecoveryWorker:Enabled"] = "false"
            });
        using var client = host.CreateClient();
        using var configuration = new FullNetApiFactory(provider, connection);
        await using var db = AiOperationBudgetAssertions.Connection(configuration);
        var token = await NativeApiE2EAssertions.LoginAsync(client, host.LogFilePath);
        var expectedEvents = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType });
        var expectedMetadataEvents = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeDocumentMetadataUpdated.EventType });
        var recoveredBases = new List<Guid>();
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
            recoveredBases.Add(knowledge.Id);
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
            // 默认自动扫描关闭：先确认旧来源事件已全部处理，再用元数据事件独立补齐投影。
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id });
            using var renamed = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeDocumentRequest("原生元数据更新", "只补意图", 3));
            Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode, await renamed.Content.ReadAsStringAsync());
            Assert.AreEqual(4, (await renamed.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!.Version);
            expectedMetadataEvents++;
            using (var metadataDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                while (true)
                {
                    var intent = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT VersionId FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id });
                    var processed = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type AND ProcessedAtUtc IS NOT NULL AND DeadLetteredAtUtc IS NULL", new { Type = AiKnowledgeDocumentMetadataUpdated.EventType });
                    var dead = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type AND DeadLetteredAtUtc IS NOT NULL", new { Type = AiKnowledgeDocumentMetadataUpdated.EventType });
                    if (dead != 0) Assert.Fail(await File.ReadAllTextAsync(worker.LogFilePath));
                    if (intent == versionId && processed == expectedMetadataEvents) break;
                    await Task.Delay(100, metadataDeadline.Token);
                }
            }
            Assert.AreEqual(expectedMetadataEvents, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeDocumentMetadataUpdated.EventType }));
            // 原生路径同时验证静态 DTO 物化、JSON 源生成及 Host/Tenant/Host 范围恢复。
            var directoryPath = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/index-changes";
            using var directory = await client.GetAsync(directoryPath + "?pageSize=1");
            Assert.AreEqual(HttpStatusCode.OK, directory.StatusCode, await directory.Content.ReadAsStringAsync());
            var confirmed = (await directory.Content.ReadFromJsonAsync<AiKnowledgeIndexChangePage>())!;
            Assert.AreEqual(1, confirmed.Items.Count); Assert.IsNull(confirmed.NextDocumentId);
            Assert.AreEqual(document.Id, confirmed.Items[0].DocumentId); Assert.AreEqual(versionId, confirmed.Items[0].VersionId);
            Assert.AreEqual("原生元数据更新", confirmed.Items[0].Title); Assert.AreEqual(2, confirmed.Items[0].VersionNumber);
            using var continued = await client.GetAsync(directoryPath + $"?afterDocumentId={document.Id}&pageSize=1");
            Assert.AreEqual(HttpStatusCode.OK, continued.StatusCode);
            Assert.AreEqual(0, (await continued.Content.ReadFromJsonAsync<AiKnowledgeIndexChangePage>())!.Items.Count);
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
        // 原事件已消费且旧 Worker 已停机；用新 Worker 验证无需人工调用恢复接口的历史补偿。
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change");
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@Id", new { Id = recoveredBases[0] });
        await using var recoveryWorker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connection, NativeAotTestTimeouts.ProcessStartup,
            additionalSettings: new Dictionary<string, string?>
            {
                ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "false",
                ["FullNet:Ai:KnowledgeIncrementalRecoveryWorker:Enabled"] = "true",
                ["FullNet:Ai:KnowledgeIncrementalRecoveryWorker:BatchSize"] = "1",
                ["FullNet:Ai:KnowledgeIncrementalRecoveryWorker:PollMilliseconds"] = "1000"
            });
        await WaitForIntentCount(2);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change AS intent INNER JOIN fn_ai_knowledge_document AS document ON document.Id=intent.DocumentId WHERE document.KnowledgeBaseId=@Id", new { Id = recoveredBases[0] }));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=1 WHERE Id=@Id", new { Id = recoveredBases[0] });
        await WaitForIntentCount(3);
        Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE VersionNumber=2"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session"));
        Assert.AreEqual(expectedEvents, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType }));
        await recoveryWorker.StopGracefullyAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        recoveryWorker.AssertNoFatalMarkersInLogs();
        await NativeIndexGrantAssertions.VerifyAsync(client);
        await host.StopGracefullyAsync(CancellationToken.None);
        host.AssertNoFatalMarkersInLogs();

        async Task WaitForIntentCount(int expected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change") != expected)
            {
                if (deadline.IsCancellationRequested) Assert.Fail(await File.ReadAllTextAsync(recoveryWorker.LogFilePath));
                await Task.Delay(100, deadline.Token);
            }
        }
    }
}
