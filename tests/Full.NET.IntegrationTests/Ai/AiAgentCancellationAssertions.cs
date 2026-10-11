using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用双库 API 夹具，验证取消事务与真实账本、租约和事件的原子性。</summary>
internal static class AiAgentCancellationAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        foreach (var tenantId in new Guid?[] { null, Guid.CreateVersion7() })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            void Bind()
            {
                if (tenantId is { } id) context.SetTenant(new TenantContext(id, "cancel-test", "取消测试"));
                else context.SetHost();
            }
            Bind();
            try
            {
                var commands = services.GetRequiredService<ICommandExecutor>();
                var queries = services.GetRequiredService<IQueryExecutor>();
                var store = services.GetRequiredService<AiAgentRunStore>();
                var service = services.GetRequiredService<AiAgentRunManagementService>();
                var budget = services.GetRequiredService<IAiOperationBudgetStore>();
                var scopeKey = tenantId?.ToString("N") ?? "host";
                if (tenantId.HasValue)
                    await commands.ExecuteAsync(new("test.ai.cancel.quota", """
                        INSERT INTO fn_ai_tenant_quota
                            (Id,TenantId,MonthlyTokenLimit,MonthlyRequestLimit,UsedTokensThisMonth,UsedRequestsThisMonth,QuotaMonthKey,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
                        VALUES (@Id,@TenantId,100000,100,0,0,@Month,1,@Now,@Now,1)
                        """, SqlDataScope.Global), AiSqlParameters.Create(("Id", Guid.CreateVersion7()), ("TenantId", tenantId),
                            ("Month", DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)), ("Now", DateTimeOffset.UtcNow)));

                foreach (var scenario in new[] { "queued", "running", "known", "missing", "late-failure", "foreign-actor", "foreign-scope", "completed" })
                {
                    var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId);
                    Bind();
                    var row = (await store.FindByIdAsync(runId))!;
                    var request = new AiOperationRequest(runId, runId, Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        "agent", new('F', 64), 100, 100, ProviderKey: "ollama", ModelId: "model");
                    if (scenario != "missing") await budget.ReserveAsync(request);
                    if (scenario is "known" or "completed") await budget.SettleAsync(runId, new(1, 1), "succeeded");
                    AgentRunLease? lease = null;
                    if (scenario != "queued")
                    {
                        lease = (await store.TryAcquireAsync(runId, "cancel-test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
                        Assert.IsTrue(await store.CommitProgressAsync(new(lease, scenario == "completed" ? "completed" : "running", null, null,
                            new(Guid.CreateVersion7(), 1, scenario == "completed" ? "run.completed" : "run.node_started", 1, "{}"))));
                        lease = lease with { Version = lease.Version + 1 };
                    }
                    var before = (await store.FindByIdAsync(runId))!;
                    var sequence = await store.FindLatestEventSequenceAsync(runId, default);
                    var quota = await ReadQuotaAsync();
                    if (scenario == "late-failure")
                    {
                        await Assert.ThrowsAsync<IOException>(() => services.GetRequiredService<ICommandTransaction>().ExecuteAsync<bool>(async token =>
                        {
                            Assert.IsTrue((await service.CancelAsync(runId, row.ActorUserId, token)).IsSuccess);
                            throw new IOException("outer transaction failed");
                        }, default));
                        Assert.AreEqual(before.StatusKey, (await store.FindByIdAsync(runId))!.StatusKey);
                        Assert.AreEqual(sequence, await store.FindLatestEventSequenceAsync(runId, default));
                        Assert.AreEqual("reserved", (await ReadBudgetAsync())!.UsageStatus);
                    }
                    if (scenario == "foreign-scope") context.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他租户"));
                    var rejected = scenario is "missing" or "foreign-actor" or "foreign-scope" or "completed";
                    string? errorCode;
                    if (scenario == "missing")
                        errorCode = (await Assert.ThrowsAsync<AiBudgetException>(() => service.CancelAsync(runId, row.ActorUserId))).Code;
                    else
                    {
                        var result = await service.CancelAsync(runId, scenario == "foreign-actor" ? Guid.CreateVersion7() : row.ActorUserId);
                        Assert.AreEqual(!rejected, result.IsSuccess, scenario);
                        errorCode = result.Error?.Code;
                    }
                    Bind();
                    if (rejected)
                    {
                        Assert.AreEqual(scenario == "missing" ? "ai.budget.operation_not_found"
                            : scenario == "completed" ? AiErrorCodes.AgentRunNotCancellable : AiErrorCodes.AgentRunNotFound, errorCode);
                        var unchanged = (await store.FindByIdAsync(runId))!;
                        Assert.AreEqual(before.StatusKey, unchanged.StatusKey, scenario);
                        Assert.AreEqual(before.Version, unchanged.Version, scenario);
                        Assert.AreEqual(before.LeaseOwner, unchanged.LeaseOwner, scenario);
                        Assert.AreEqual(sequence, await store.FindLatestEventSequenceAsync(runId, default), scenario);
                        Assert.AreEqual(quota, await ReadQuotaAsync(), scenario);
                        continue;
                    }
                    var cancelled = (await store.FindByIdAsync(runId))!;
                    Assert.AreEqual("cancelled", cancelled.StatusKey, scenario);
                    Assert.IsNull(cancelled.LeaseOwner, scenario);
                    Assert.IsNull(cancelled.LeaseExpiresAtUtc, scenario);
                    Assert.AreEqual(before.Version + 1, cancelled.Version, scenario);
                    Assert.AreEqual(sequence + 1, await store.FindLatestEventSequenceAsync(runId, default), scenario);
                    Assert.AreEqual(1L, await queries.QuerySingleOrDefaultAsync<long>(new("test.ai.cancel.events",
                        "SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@RunId AND EventType='run.cancelled'", SqlDataScope.Global),
                        AiSqlParameters.Create(("RunId", runId))));
                    var ledger = (await ReadBudgetAsync())!;
                    Assert.AreEqual(scenario == "known" ? "known" : "unknown", ledger.UsageStatus, scenario);
                    Assert.AreEqual(scenario == "known" ? "succeeded" : "cancelled", ledger.Outcome, scenario);
                    Assert.AreEqual(scenario == "known" ? (long?)1 : null, ledger.InputTokens, scenario);
                    Assert.AreEqual(scenario == "known" ? (long?)1 : null, ledger.OutputTokens, scenario);
                    Assert.AreEqual(quota, await ReadQuotaAsync(), scenario);
                    Assert.AreEqual(AiErrorCodes.AgentRunNotCancellable, (await service.CancelAsync(runId, row.ActorUserId)).Error!.Code);
                    if (lease is not null)
                    {
                        Assert.IsFalse(await store.RenewAsync(lease, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
                        Assert.IsFalse(await store.CommitProgressAsync(new(lease, "completed", null, null,
                            new(Guid.CreateVersion7(), sequence + 2, "run.completed", 1, "{}"))));
                    }
                    Assert.AreEqual(sequence + 1, await store.FindLatestEventSequenceAsync(runId, default), scenario);

                    Task<AiOperationBudgetRecord?> ReadBudgetAsync() => queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(
                        AiOperationBudgetSql.Find, AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", scopeKey)));
                    Task<long> ReadQuotaAsync() => tenantId.HasValue ? queries.QuerySingleOrDefaultAsync<long>(new("test.ai.cancel.quota_tokens",
                        "SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@TenantId", SqlDataScope.Global), AiSqlParameters.Create(("TenantId", tenantId))) : Task.FromResult(0L);
                }
            }
            finally { context.Clear(); }
        }
    }
}
