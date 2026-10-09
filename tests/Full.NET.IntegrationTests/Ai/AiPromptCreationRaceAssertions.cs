using System.Data.Common;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Features.ManageModelConfigs;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>固定预留与插入之间的竞争时序，真实双库验证未执行落败请求的零计量与赢家保护。</summary>
internal static class AiPromptCreationRaceAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, DbConnection db, Guid modelId, Guid versionId, Guid originalRunId)
    {
        var row = await db.QuerySingleAsync<AiAgentRunRecord>("SELECT * FROM fn_ai_agent_run WHERE Id=@Id", new { Id = originalRunId });
        var binding = new SessionBindingSnapshot(row.ActorUserId, row.TenantId, row.SessionId, row.SecurityStamp, row.ActorScope, row.EffectiveScope, row.SessionKind);
        var priceId = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_ai_model_price (Id,ModelConfigId,ProviderKey,ModelId,Currency,InputPerMillion,OutputPerMillion,CachedInputPerMillion,ValidFromUtc)
            VALUES (@Id,@Model,'ollama','model','USD',2,4,1,@Now)
            """, new { Id = priceId, Model = modelId, Now = DateTimeOffset.UtcNow.AddDays(-1) });
        foreach (var scenario in new[] { "priced_mixed", "unpriced_mixed", "unpriced_replay" })
        {
            if (scenario == "unpriced_mixed") await db.ExecuteAsync("DELETE FROM fn_ai_model_price WHERE Id=@Id", new { Id = priceId });
            await using var scope = factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider; var context = services.GetRequiredService<ICurrentTenantContextWriter>(); context.SetHost();
            try
            {
                var normal = services.GetRequiredService<AiAgentRunManagementService>();
                var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-single-text-v1", modelId, "race input", 100, 100,
                    scenario == "priced_mixed" ? versionId : null);
                Guid winnerId = default;
                var hook = new AfterReserveBudget(services.GetRequiredService<IAiOperationBudgetStore>(), async () =>
                {
                    var winner = await normal.CreateAsync(request, binding); Assert.IsTrue(winner.IsSuccess); winnerId = winner.Value!.RunId;
                });
                var outer = new AiAgentRunManagementService(services.GetRequiredService<AiAgentRunStore>(), services.GetRequiredService<AiAgentRunReadiness>(),
                    services.GetRequiredService<AiAgentRunApprovalGate>(), services.GetRequiredService<AiModelConfigQueryService>(), services.GetRequiredService<IBackgroundSessionBindingValidator>(),
                    services.GetRequiredService<AiPromptRunResolver>(), hook, services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(), services.GetRequiredService<IIdGenerator>(), services.GetRequiredService<IOptions<AiAgentRuntimeOptions>>());
                // 外层已查过幂等键并完成预留，内层插入赢家；保证每次都生成且结算一个真实孤立预算。
                var loserRequest = scenario == "unpriced_replay" ? request : request with { PromptVersionId = request.PromptVersionId.HasValue ? null : versionId };
                var result = await outer.CreateAsync(loserRequest, binding);
                Assert.AreEqual(scenario == "unpriced_replay", result.IsSuccess);
                if (result.IsSuccess) Assert.AreEqual(winnerId, result.Value!.RunId);
                else Assert.AreEqual("ai.prompts.conflict", result.Error!.Code);
                Assert.AreNotEqual(winnerId, hook.OperationId);
                Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_run WHERE Id=@Id", new { Id = hook.OperationId }));
                var receipt = await db.QuerySingleAsync<Receipt>("SELECT UsageStatus,Outcome,InputTokens,OutputTokens,ChargedTokens,ChargedCost FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = hook.OperationId });
                Assert.AreEqual("known", receipt.UsageStatus); Assert.AreEqual("cancelled", receipt.Outcome);
                Assert.AreEqual(0L, receipt.InputTokens); Assert.AreEqual(0L, receipt.OutputTokens); Assert.AreEqual(0L, receipt.ChargedTokens);
                Assert.AreEqual(scenario == "priced_mixed" ? (decimal?)0 : null, receipt.ChargedCost);
                var winnerReceipt = await db.QuerySingleAsync<Receipt>("SELECT UsageStatus,Outcome,InputTokens,OutputTokens,ChargedTokens,ChargedCost FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = winnerId });
                Assert.AreEqual("reserved", winnerReceipt.UsageStatus); Assert.AreEqual(200L, winnerReceipt.ChargedTokens);
                Assert.AreEqual(scenario == "priced_mixed" ? (decimal?)0.0006m : null, winnerReceipt.ChargedCost);
                Assert.IsTrue((await normal.CancelAsync(winnerId, binding.UserId)).IsSuccess);
                var cancelledWinner = await db.QuerySingleAsync<Receipt>("SELECT UsageStatus,Outcome,InputTokens,OutputTokens,ChargedTokens,ChargedCost FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = winnerId });
                Assert.AreEqual("unknown", cancelledWinner.UsageStatus); Assert.AreEqual("cancelled", cancelledWinner.Outcome);
                Assert.AreEqual(200L, cancelledWinner.ChargedTokens); Assert.AreEqual(winnerReceipt.ChargedCost, cancelledWinner.ChargedCost);
            }
            finally { context.Clear(); }
        }
    }

    private sealed class AfterReserveBudget(IAiOperationBudgetStore inner, Func<Task> afterReserve) : IAiOperationBudgetStore
    {
        internal Guid OperationId { get; private set; }
        public async Task<AiOperationReservation> ReserveAsync(AiOperationRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.ReserveAsync(request, cancellationToken); OperationId = request.OperationId;
            await afterReserve(); return result;
        }
        public Task SettleAsync(Guid operationId, AiOperationUsage usage, string outcome, CancellationToken cancellationToken = default) => inner.SettleAsync(operationId, usage, outcome, cancellationToken);
    }

    private sealed class Receipt
    {
        public string UsageStatus { get; set; } = "";
        public string Outcome { get; set; } = "";
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long ChargedTokens { get; set; }
        public decimal? ChargedCost { get; set; }
    }
}
