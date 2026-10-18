using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用原双库运行夹具；自动对账仅结算原完成回执，坏凭据和未知用量保留原预留。</summary>
internal static partial class AiAgentCompletionAssertions
{
    private static async Task VerifyAutomaticCompletedReceiptsAsync(FullNetApiFactory factory, IServiceProvider services, Guid? tenantId)
    {
        var scope = tenantId?.ToString("N") ?? "host";
        var context = services.GetRequiredService<ICurrentTenantContextWriter>();
        void Bind() { if (tenantId is { } id) context.SetTenant(new(id, "auto-receipt", "自动回执")); else context.SetHost(); }
        Bind();
        var store = services.GetRequiredService<AiAgentRunStore>(); var commands = services.GetRequiredService<ICommandExecutor>();
        var query = services.GetRequiredService<IQueryExecutor>(); var budget = services.GetRequiredService<IAiOperationBudgetStore>();
        var reconciler = ActivatorUtilities.CreateInstance<AiAgentCompletedReceiptReconciliation>(services);
        foreach (var scenario in new[] { "completed", "unknown-ledger", "started", "corrupt" })
        {
            var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId); Bind();
            var run = (await store.FindByIdAsync(runId))!; var modelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            await budget.ReserveAsync(new(runId, runId, modelId, "agent", new('F', 64), 100, 100, ProviderKey: "ollama", ModelId: "model"));
            var lease = (await store.TryAcquireAsync(runId, "auto-receipt", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
            var state = AiModelRunCheckpoint.Start(run, new() { Id = modelId, Version = 1 }, "");
            var payload = scenario == "started" ? state.ToJson() : state.Complete(new("not-released", JsonSerializer.SerializeToElement(true), 2, 1)).ToJson();
            Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
                new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload,
                    scenario == "corrupt" ? new('0', 64) : AiModelRunCheckpoint.Digest(payload)),
                new(Guid.CreateVersion7(), 1, "run.node_completed", 1, "{}"))));
            await commands.ExecuteAsync(new("test.ai.auto_receipt.reconciliation", "UPDATE fn_ai_agent_run SET StatusKey='reconciliation_required',Version=Version+1,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@RunId", SqlDataScope.Global), AiSqlParameters.Create(("RunId", runId)));
            if (scenario == "unknown-ledger") await budget.SettleAsync(runId, new(null, null), "failed");
            var before = (await store.FindByIdAsync(runId))!;
            var expected = scenario is "completed" or "unknown-ledger";
            Assert.AreEqual(expected, await reconciler.ReconcileAsync(runId, default), scenario);
            var current = (await store.FindByIdAsync(runId))!;
            Assert.AreEqual(expected ? "failed" : "reconciliation_required", current.StatusKey);
            var operation = (await query.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
                AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", scope))))!;
            if (expected)
            {
                Assert.AreEqual("known", operation.UsageStatus); Assert.AreEqual(2L, operation.InputTokens); Assert.AreEqual(1L, operation.OutputTokens);
                Assert.AreEqual(before.Version + 1, current.Version);
                Assert.IsFalse(await reconciler.ReconcileAsync(runId, default));
            }
            else { Assert.AreEqual("reserved", operation.UsageStatus); Assert.AreEqual(before.Version, current.Version); }
        }
    }
}
