using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证人工关闭、原月份账本、幂等证据、所有权及终态事件的同事务语义。</summary>
internal static class AiAgentReconciliationAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        var permissions = new[] { AiModelPermissions.Read, AiModelPermissions.Create, AiAgentRunPermissions.Create,
            AiAgentRunPermissions.Read, AiAgentRunPermissions.Reconcile };
        var owner = await factory.CreateHostIdentityAsync($"reconcile-owner-{Guid.NewGuid():N}", permissions);
        var other = await factory.CreateHostIdentityAsync($"reconcile-other-{Guid.NewGuid():N}", permissions);
        var reader = await factory.CreateHostIdentityAsync($"reconcile-reader-{Guid.NewGuid():N}",
            [AiAgentRunPermissions.Read, AiAgentRunPermissions.Create]);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "对账模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));

        foreach (var known in new[] { false, true })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
            using var createdResponse = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(
                Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id, "fixture", 100, 100));
            Assert.AreEqual(HttpStatusCode.Accepted, createdResponse.StatusCode);
            var runId = (await createdResponse.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!.RunId;
            // 只构造待核证的已派发历史；不会访问真实外部模型或伪造成功检查点。
            await connection.ExecuteAsync("UPDATE fn_ai_agent_run SET StatusKey = 'reconciliation_required', Version = Version + 1 WHERE Id = @Id", new { Id = runId });
            await connection.ExecuteAsync("""
                UPDATE fn_ai_operation_budget SET UsageStatus = 'unknown', Outcome = 'failed', ChargedTokens = ReservedTokens,
                    QuotaMonthKey = '2000-01'
                WHERE Id = @Id
                """, new { Id = runId });
            var originalMonth = await connection.QuerySingleAsync<string>("SELECT QuotaMonthKey FROM fn_ai_operation_budget WHERE Id = @Id", new { Id = runId });
            var url = $"/api/v1/ai/agent/runs/{runId}/reconciliation";
            var snapshot = (await client.GetFromJsonAsync<AiAgentReconciliationResponse>(url))!;
            Assert.AreEqual("unknown", snapshot.UsageStatus); Assert.AreEqual(200L, snapshot.ReservedTokens); Assert.IsTrue(snapshot.CanReconcile);
            var request = new ReconcileAiAgentRunRequest(Guid.CreateVersion7(), snapshot.Version, new('A', 64), known ? 10 : null, known ? 2 : null, known ? 3 : null);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reader.AccessToken);
            using var denied = await client.PostAsJsonAsync(url, request); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
            using var foreign = await client.GetAsync(url); Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
            using var stale = await client.PostAsJsonAsync(url, request with { ExpectedVersion = snapshot.Version + 1 }); Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);

            using var settled = await client.PostAsJsonAsync(url, request);
            Assert.IsTrue(settled.IsSuccessStatusCode, await settled.Content.ReadAsStringAsync());
            using var replay = await client.PostAsJsonAsync(url, request); Assert.IsTrue(replay.IsSuccessStatusCode);
            using var conflict = await client.PostAsJsonAsync(url, request with { ClientRequestId = Guid.CreateVersion7() }); Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
            var final = (await client.GetFromJsonAsync<AiAgentReconciliationResponse>(url))!;
            Assert.AreEqual("failed", final.StatusKey); Assert.IsFalse(final.CanReconcile);
            Assert.AreEqual(known ? "known" : "unknown", final.UsageStatus); Assert.AreEqual(originalMonth, final.QuotaMonthKey);
            Assert.AreEqual(known ? 10L : (long?)null, final.InputTokens); Assert.AreEqual(known ? 3L : (long?)null, final.CachedInputTokens);
            var count = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId = @Id AND EventType IN ('run.reconciled', 'run.failed')", new { Id = runId });
            Assert.AreEqual(2, count);
            var charged = await connection.QuerySingleAsync<long>("SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id = @Id", new { Id = runId });
            Assert.AreEqual(known ? 12L : 200L, charged);
        }
    }
}
