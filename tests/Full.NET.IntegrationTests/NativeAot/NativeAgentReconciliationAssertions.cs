using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>在原生 HTTP 宿主核对人工对账闭包；历史夹具不代表真实模型计量或外部动作已核实。</summary>
internal static class NativeAgentReconciliationAssertions
{
    internal static async Task VerifyAsync(HttpClient client, string accessToken, DatabaseProvider provider,
        string connectionString, CancellationToken cancellationToken)
    {
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            await using DbConnection connection = provider == DatabaseProvider.SqlServer
                ? new SqlConnection(connectionString)
                : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString,
                    MySqlGuidStorageMode.Binary16, allowUserVariables: false));
            await SeedHeartbeatAsync(connection, cancellationToken).ConfigureAwait(false);
            using var createdModel = await client.PostAsJsonAsync("/api/v1/ai/model-configs",
                new CreateAiModelConfigRequest(null, "原生人工对账模型", "ollama", "https://provider.test",
                    "model", null, null, false, true), cancellationToken).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Created, createdModel.StatusCode, await createdModel.Content.ReadAsStringAsync(cancellationToken));
            var model = (await createdModel.Content.ReadFromJsonAsync<AiModelConfigResponse>(cancellationToken).ConfigureAwait(false))!;

            foreach (var known in new[] { false, true })
            {
                // 只登记当前原生宿主创建的运行，再构造待核证历史；没有 Worker 或外部模型派发。
                await SeedHeartbeatAsync(connection, cancellationToken).ConfigureAwait(false);
                using var createdRun = await client.PostAsJsonAsync("/api/v1/ai/agent/runs",
                    new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", model.Id,
                        "native-reconciliation-fixture", 100, 100), cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.Accepted, createdRun.StatusCode, await createdRun.Content.ReadAsStringAsync(cancellationToken));
                var runId = (await createdRun.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>(cancellationToken).ConfigureAwait(false))!.RunId;
                Assert.AreEqual(1, await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE fn_ai_agent_run SET StatusKey = 'reconciliation_required', Version = Version + 1 WHERE Id = @Id",
                    new { Id = runId }, cancellationToken: cancellationToken)).ConfigureAwait(false));
                Assert.AreEqual(1, await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE fn_ai_operation_budget SET UsageStatus = 'unknown', Outcome = 'failed', ChargedTokens = ReservedTokens WHERE Id = @Id",
                    new { Id = runId }, cancellationToken: cancellationToken)).ConfigureAwait(false));

                var url = $"/api/v1/ai/agent/runs/{runId}/reconciliation";
                var original = (await client.GetFromJsonAsync<AiAgentReconciliationResponse>(url, cancellationToken).ConfigureAwait(false))!;
                Assert.AreEqual("reconciliation_required", original.StatusKey);
                Assert.AreEqual("unknown", original.UsageStatus);
                Assert.AreEqual(200L, original.ReservedTokens);
                Assert.IsNull(original.InputTokens);
                Assert.IsTrue(original.CanReconcile);
                var request = new ReconcileAiAgentRunRequest(Guid.CreateVersion7(), original.Version,
                    new string('A', 64), known ? 10 : null, known ? 2 : null, known ? 3 : null);
                using var stale = await client.PostAsJsonAsync(url, request with { ExpectedVersion = original.Version + 1 }, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
                using var settled = await client.PostAsJsonAsync(url, request, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, settled.StatusCode, await settled.Content.ReadAsStringAsync(cancellationToken));
                Assert.AreEqual(true, await settled.Content.ReadFromJsonAsync<bool>(cancellationToken).ConfigureAwait(false));
                using var replay = await client.PostAsJsonAsync(url, request, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
                using var conflict = await client.PostAsJsonAsync(url, request with { ClientRequestId = Guid.CreateVersion7() }, cancellationToken).ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);

                var final = (await client.GetFromJsonAsync<AiAgentReconciliationResponse>(url, cancellationToken).ConfigureAwait(false))!;
                Assert.AreEqual("failed", final.StatusKey);
                Assert.AreEqual(original.Version + 1, final.Version);
                Assert.IsFalse(final.CanReconcile);
                Assert.AreEqual(original.QuotaMonthKey, final.QuotaMonthKey);
                Assert.AreEqual(known ? "known" : "unknown", final.UsageStatus);
                Assert.AreEqual(known ? 10L : (long?)null, final.InputTokens);
                Assert.AreEqual(known ? 2L : (long?)null, final.OutputTokens);
                Assert.AreEqual(known ? 3L : (long?)null, final.CachedInputTokens);
                Assert.AreEqual(known ? 12L : 200L, await connection.QuerySingleAsync<long>(new CommandDefinition(
                    "SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id = @Id", new { Id = runId }, cancellationToken: cancellationToken)).ConfigureAwait(false));
                Assert.AreEqual(2, await connection.QuerySingleAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId = @Id AND EventType IN ('run.reconciled', 'run.failed')",
                    new { Id = runId }, cancellationToken: cancellationToken)).ConfigureAwait(false));
            }
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = previousAuthorization;
        }
    }

    private static async Task SeedHeartbeatAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_ai_agent_worker_instance
                (InstanceId, WorkerRole, RuntimeVersion, HostProfile, StartedAtUtc, LastHeartbeatAtUtc)
            VALUES (@InstanceId, 'ai-agent', '1', 'native-reconciliation-fixture', @Now, @Now)
            """, new { InstanceId = Guid.CreateVersion7(), Now = now }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
