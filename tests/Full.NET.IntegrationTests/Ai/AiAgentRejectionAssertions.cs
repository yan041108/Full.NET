using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

internal static partial class AiAgentCompletionAssertions
{
    /// <summary>同一双库夹具验证可结算拒绝的原子性；租户兼容配额不能被单独退款。</summary>
    private static async Task VerifyRejectionsAsync(FullNetApiFactory factory)
    {
        foreach (var tenantId in new Guid?[] { null, Guid.CreateVersion7() })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
            void Bind()
            {
                if (tenantId is { } id) context.SetTenant(new TenantContext(id, "rejection-test", "拒绝测试"));
                else context.SetHost();
            }
            Bind();
            try
            {
                var commands = provider.GetRequiredService<ICommandExecutor>();
                var queries = provider.GetRequiredService<IQueryExecutor>();
                var store = provider.GetRequiredService<AiAgentRunStore>();
                var completion = provider.GetRequiredService<AiAgentRunCompletion>();
                var budget = provider.GetRequiredService<IAiOperationBudgetStore>();
                var scopeKey = tenantId?.ToString("N") ?? "host";
                if (tenantId.HasValue)
                    await commands.ExecuteAsync(new("test.ai.rejection.quota", """
                        INSERT INTO fn_ai_tenant_quota
                            (Id,TenantId,MonthlyTokenLimit,MonthlyRequestLimit,UsedTokensThisMonth,UsedRequestsThisMonth,QuotaMonthKey,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
                        VALUES (@Id,@TenantId,100000,100,0,0,@Month,1,@Now,@Now,1)
                        """, SqlDataScope.Global), AiSqlParameters.Create(("Id", Guid.CreateVersion7()), ("TenantId", tenantId),
                            ("Month", DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)), ("Now", DateTimeOffset.UtcNow)));

                foreach (var status in new[] { "failed", "authorization_required" })
                foreach (var scenario in new[] { "normal", "missing", "known-ledger", "wrong-scope", "late-failure", "event-failure", "stale-lease" })
                {
                    var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId);
                    Bind();
                    var request = new AiOperationRequest(runId, runId, Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        "agent", new('E', 64), 100, 100, ProviderKey: "ollama", ModelId: "model");
                    if (scenario != "missing") Assert.IsTrue((await budget.ReserveAsync(request)).IsNew);
                    if (scenario == "known-ledger") await budget.SettleAsync(runId, new(1, 1), "succeeded");
                    var lease = (await store.TryAcquireAsync(runId, "rejection-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                    const string beforePayload = """{"marker":"before"}""";
                    const string afterPayload = """{"marker":"rejected"}""";
                    Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
                        new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, 1, beforePayload, AiModelRunCheckpoint.Digest(beforePayload)),
                        new(Guid.CreateVersion7(), 1, "run.node_started", 1, "{}"))));
                    lease = lease with { Version = lease.Version + 1 };
                    var progress = new AgentRunProgressCommit(lease, status, null,
                        new(Guid.CreateVersion7(), 2, 1, AgentFrameworkRuntime.FrameworkVersion, 1, afterPayload, AiModelRunCheckpoint.Digest(afterPayload)),
                        new(Guid.CreateVersion7(), 2, "run." + status, 1, "{}"));
                    var before = await ReadBudgetAsync();
                    var quotaBefore = await ReadQuotaAsync();
                    if (scenario is not ("normal" or "known-ledger"))
                    {
                        if (scenario is "missing" or "wrong-scope")
                        {
                            if (scenario == "wrong-scope") context.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他租户"));
                            var error = await Assert.ThrowsAsync<AiBudgetException>(() => completion.RejectAsync(progress, CancellationToken.None));
                            Assert.AreEqual("ai.budget.operation_not_found", error.Code, scenario);
                            Bind();
                        }
                        else if (scenario == "late-failure")
                            await Assert.ThrowsAsync<IOException>(() => provider.GetRequiredService<ICommandTransaction>().ExecuteAsync<bool>(async token =>
                            {
                                Assert.IsTrue(await completion.RejectAsync(progress, token));
                                throw new IOException("physical rejection boundary failed");
                            }, CancellationToken.None));
                        else if (scenario == "event-failure")
                        {
                            var error = await Assert.ThrowsAsync<DataCommandException>(() => completion.RejectAsync(
                                progress with { Event = progress.Event! with { Sequence = 1 } }, CancellationToken.None));
                            Assert.AreEqual(DataCommandFailureKind.UniqueConstraint, error.Kind);
                        }
                        else Assert.IsFalse(await completion.RejectAsync(progress with { Lease = lease with { Epoch = lease.Epoch - 1 } }, CancellationToken.None));
                        var run = (await store.FindByIdAsync(runId))!;
                        Assert.AreEqual("running", run.StatusKey, scenario);
                        Assert.AreEqual(lease.Version, run.Version, scenario);
                        Assert.AreEqual(1L, (await store.FindLatestCheckpointAsync(runId))!.Sequence, scenario);
                        Assert.AreEqual(1L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None), scenario);
                        var after = await ReadBudgetAsync();
                        Assert.AreEqual(before?.UsageStatus, after?.UsageStatus, scenario);
                        Assert.AreEqual(before?.Outcome, after?.Outcome, scenario);
                        Assert.AreEqual(before?.InputTokens, after?.InputTokens, scenario);
                        Assert.AreEqual(quotaBefore, await ReadQuotaAsync(), scenario);
                        if (scenario == "missing") Assert.IsTrue((await budget.ReserveAsync(request)).IsNew);
                        await commands.ExecuteAsync(new("test.ai.rejection.expire", "UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc=@Expired WHERE Id=@RunId", SqlDataScope.Global),
                            AiSqlParameters.Create(("RunId", runId), ("Expired", DateTimeOffset.UtcNow.AddMinutes(-1))));
                        var recovered = (await store.TryAcquireAsync(runId, "rejection-recovery", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                        Assert.IsTrue(recovered.Epoch > lease.Epoch);
                        Assert.IsFalse(await completion.RejectAsync(progress, CancellationToken.None));
                        progress = progress with { Lease = recovered };
                    }
                    Assert.IsTrue(await completion.RejectAsync(progress, CancellationToken.None));
                    Assert.AreEqual(status, (await store.FindByIdAsync(runId))!.StatusKey, scenario);
                    Assert.AreEqual(2L, (await store.FindLatestCheckpointAsync(runId))!.Sequence, scenario);
                    Assert.AreEqual(2L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None), scenario);
                    var settled = (await ReadBudgetAsync())!;
                    // 未知回执不降级已知计量；账本保持原结果，拒绝状态仍可原子收敛。
                    var known = scenario == "known-ledger";
                    Assert.AreEqual(known ? "known" : "unknown", settled.UsageStatus, scenario);
                    Assert.AreEqual(known ? "succeeded" : status == "failed" ? "failed" : "cancelled", settled.Outcome, scenario);
                    Assert.AreEqual(known ? (long?)1 : null, settled.InputTokens, scenario);
                    Assert.AreEqual(known ? (long?)1 : null, settled.OutputTokens, scenario);
                    Assert.AreEqual(known ? 2L : 200L, await queries.QuerySingleOrDefaultAsync<long>(
                        new("test.ai.rejection.charged", "SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@RunId", SqlDataScope.Global), AiSqlParameters.Create(("RunId", runId))));
                    var finalQuota = await ReadQuotaAsync();
                    if (tenantId.HasValue) Assert.AreEqual(quotaBefore + (scenario == "missing" ? 200 : 0), finalQuota, scenario);
                    Assert.IsFalse(await completion.RejectAsync(progress, CancellationToken.None));
                    Assert.AreEqual(finalQuota, await ReadQuotaAsync(), scenario);
                    Assert.IsNull(await store.TryAcquireAsync(runId, "rejected-again", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));

                    Task<AiOperationBudgetRecord?> ReadBudgetAsync() => queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(
                        AiOperationBudgetSql.Find, AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", scopeKey)));
                    Task<long> ReadQuotaAsync() => tenantId.HasValue ? queries.QuerySingleOrDefaultAsync<long>(
                        new("test.ai.rejection.quota_tokens", "SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@TenantId", SqlDataScope.Global),
                        AiSqlParameters.Create(("TenantId", tenantId))) : Task.FromResult(0L);
                }
            }
            finally { context.Clear(); }
        }
    }
}
