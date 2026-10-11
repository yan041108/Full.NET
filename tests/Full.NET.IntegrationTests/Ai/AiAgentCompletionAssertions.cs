using System.Text.Json;
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

/// <summary>使用生产 DI 和真实双库事务，验证成功终态、预算、兼容配额及凭证共同提交或回滚。</summary>
internal static partial class AiAgentCompletionAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        foreach (var tenantId in new Guid?[] { null, Guid.CreateVersion7() })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
            void Bind()
            {
                if (tenantId is { } id) context.SetTenant(new TenantContext(id, "completion-test", "完成测试"));
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
                    await commands.ExecuteAsync(new("test.ai.completion.quota", """
                        INSERT INTO fn_ai_tenant_quota
                            (Id,TenantId,MonthlyTokenLimit,MonthlyRequestLimit,UsedTokensThisMonth,UsedRequestsThisMonth,QuotaMonthKey,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
                        VALUES (@Id,@TenantId,100000,100,0,0,@Month,1,@Now,@Now,1)
                        """, SqlDataScope.Global), AiSqlParameters.Create(("Id", Guid.CreateVersion7()), ("TenantId", tenantId),
                            ("Month", DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)), ("Now", DateTimeOffset.UtcNow)));

                foreach (var scenario in new[] { "known", "unknown", "missing", "conflict", "wrong-scope", "late-failure", "event-failure", "stale-lease" })
                {
                    var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId);
                    Bind();
                    var run = (await store.FindByIdAsync(runId))!;
                    var modelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
                    var request = new AiOperationRequest(runId, runId, modelId, "agent", new('D', 64), 100, 100,
                        ProviderKey: "ollama", ModelId: "model");
                    if (scenario != "missing") Assert.IsTrue((await budget.ReserveAsync(request)).IsNew);
                    if (scenario == "conflict") await budget.SettleAsync(runId, new(1, 1), "succeeded");
                    var lease = (await store.TryAcquireAsync(runId, "completion-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                    AiOperationUsage usage = scenario == "unknown" ? new(null, 1) : scenario == "conflict" ? new(1, 1) : new(2, 1);
                    var receipt = AiModelRunCheckpoint.Start(run, new() { Id = modelId, Version = 1 }, "")
                        .Complete(new("done", JsonSerializer.SerializeToElement(true), usage.InputTokens, usage.OutputTokens));
                    var payload = receipt.ToJson();
                    Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
                        new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload, AiModelRunCheckpoint.Digest(payload)),
                        new(Guid.CreateVersion7(), 1, "run.node_completed", 1, "{}"))));
                    lease = lease with { Version = lease.Version + 1 };
                    var progress = new AgentRunProgressCommit(lease, "completed",
                        new(Guid.CreateVersion7(), "model.text", 1, runId, "committed", 1, null, null,
                            usage.InputTokens, usage.OutputTokens, null, null, null, receipt.InputDigest, null),
                        new(Guid.CreateVersion7(), 2, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload, AiModelRunCheckpoint.Digest(payload)),
                        new(Guid.CreateVersion7(), 2, "run.completed", 1, "{}"));
                    var before = await ReadBudgetAsync();
                    var quotaBefore = await ReadQuotaAsync();
                    if (scenario is "known" or "unknown")
                        Assert.IsTrue(await completion.CommitAsync(progress, usage, CancellationToken.None));
                    else
                    {
                        if (scenario is "missing" or "conflict" or "wrong-scope")
                        {
                            if (scenario == "wrong-scope") context.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他租户"));
                            var error = await Assert.ThrowsAsync<AiBudgetException>(() => completion.CommitAsync(progress,
                                scenario == "conflict" ? new(2, 1) : usage, CancellationToken.None));
                            Assert.AreEqual(scenario == "conflict" ? "ai.budget.settlement_conflict" : "ai.budget.operation_not_found", error.Code);
                            Bind();
                        }
                        else if (scenario == "late-failure")
                        {
                            // 模拟预算和状态均写完、外层物理提交之前的故障，不能只回滚其中一个嵌套操作。
                            await Assert.ThrowsAsync<IOException>(() => provider.GetRequiredService<ICommandTransaction>().ExecuteAsync<bool>(async token =>
                            {
                                Assert.IsTrue(await completion.CommitAsync(progress, usage, token));
                                throw new IOException("physical completion boundary failed");
                            }, CancellationToken.None));
                        }
                        else if (scenario == "event-failure")
                        {
                            var error = await Assert.ThrowsAsync<DataCommandException>(() => completion.CommitAsync(
                                progress with { Event = progress.Event! with { Sequence = 1 } }, usage, CancellationToken.None));
                            Assert.AreEqual(DataCommandFailureKind.UniqueConstraint, error.Kind);
                        }
                        else
                            Assert.IsFalse(await completion.CommitAsync(progress with { Lease = lease with { Epoch = lease.Epoch - 1 } }, usage, CancellationToken.None));

                        Assert.AreEqual("running", (await store.FindByIdAsync(runId))!.StatusKey, scenario);
                        Assert.AreEqual(lease.Version, (await store.FindByIdAsync(runId))!.Version, scenario);
                        Assert.AreEqual(1L, (await store.FindLatestCheckpointAsync(runId))!.Sequence, scenario);
                        Assert.AreEqual(1L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None), scenario);
                        Assert.AreEqual(0L, await queries.QuerySingleOrDefaultAsync<long>(new("test.ai.completion.steps", "SELECT COUNT(*) FROM fn_ai_agent_step WHERE RunId=@RunId", SqlDataScope.Global), AiSqlParameters.Create(("RunId", runId))), scenario);
                        var after = await ReadBudgetAsync();
                        Assert.AreEqual(before?.UsageStatus, after?.UsageStatus, scenario);
                        Assert.AreEqual(before?.InputTokens, after?.InputTokens, scenario);
                        Assert.AreEqual(before?.Outcome, after?.Outcome, scenario);
                        Assert.AreEqual(quotaBefore, await ReadQuotaAsync(), scenario);
                        if (scenario == "missing") Assert.IsTrue((await budget.ReserveAsync(request)).IsNew);

                        // 回滚后已有结果凭证仍可被新租约领取；旧租约不得提交，新租约仅结算原结果。
                        await commands.ExecuteAsync(new("test.ai.completion.expire", "UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc=@Expired WHERE Id=@RunId", SqlDataScope.Global),
                            AiSqlParameters.Create(("RunId", runId), ("Expired", DateTimeOffset.UtcNow.AddMinutes(-1))));
                        Assert.IsTrue((await store.ScanQueuedAsync(100, DateTimeOffset.UtcNow)).Contains(runId));
                        var recovered = (await store.TryAcquireAsync(runId, "completion-recovery", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                        Assert.IsTrue(recovered.Epoch > lease.Epoch);
                        Assert.IsFalse(await completion.CommitAsync(progress, usage, CancellationToken.None));
                        progress = progress with { Lease = recovered };
                        Assert.IsTrue(await completion.CommitAsync(progress, usage, CancellationToken.None));
                    }
                    Assert.AreEqual("completed", (await store.FindByIdAsync(runId))!.StatusKey, scenario);
                    Assert.AreEqual(2L, (await store.FindLatestCheckpointAsync(runId))!.Sequence, scenario);
                    var settled = (await ReadBudgetAsync())!;
                    Assert.AreEqual(scenario == "unknown" ? "unknown" : "known", settled.UsageStatus, scenario);
                    Assert.AreEqual("succeeded", settled.Outcome, scenario);
                    Assert.AreEqual(scenario == "unknown" ? null : usage.InputTokens, settled.InputTokens, scenario);
                    Assert.AreEqual(scenario == "unknown" ? 200L : usage.InputTokens + usage.OutputTokens,
                        await queries.QuerySingleOrDefaultAsync<long>(new("test.ai.completion.charged", "SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id=@RunId", SqlDataScope.Global),
                            AiSqlParameters.Create(("RunId", runId))), scenario);
                    var finalQuota = await ReadQuotaAsync();
                    if (tenantId.HasValue)
                        Assert.AreEqual(quotaBefore + (scenario == "missing" ? 3 : scenario is "unknown" or "conflict" ? 0 : -197), finalQuota, scenario);
                    Assert.IsFalse(await completion.CommitAsync(progress, usage, CancellationToken.None));
                    Assert.AreEqual(finalQuota, await ReadQuotaAsync(), scenario);
                    Assert.IsNull(await store.TryAcquireAsync(runId, "completed-again", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));

                    Task<AiOperationBudgetRecord?> ReadBudgetAsync() => queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(
                        AiOperationBudgetSql.Find, AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", scopeKey)));
                    Task<long> ReadQuotaAsync() => tenantId.HasValue ? queries.QuerySingleOrDefaultAsync<long>(
                        new("test.ai.completion.quota_tokens", "SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@TenantId", SqlDataScope.Global),
                        AiSqlParameters.Create(("TenantId", tenantId))) : Task.FromResult(0L);
                }
            }
            finally { context.Clear(); }
        }
        await VerifyRejectionsAsync(factory);
        await VerifyTerminalBudgetRepairAsync(factory);
    }
}
