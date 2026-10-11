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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用双库完成夹具，验证历史账本修复、并发占有、旧月份配额和外层回滚。</summary>
internal static partial class AiAgentCompletionAssertions
{
    private static async Task VerifyTerminalBudgetRepairAsync(FullNetApiFactory factory)
    {
        foreach (var tenantId in new Guid?[] { null, Guid.CreateVersion7() })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
            void Bind()
            {
                if (tenantId is { } id) context.SetTenant(new(id, "budget-repair", "预算修复"));
                else context.SetHost();
            }
            Bind();
            try
            {
                var commands = provider.GetRequiredService<ICommandExecutor>();
                var queries = provider.GetRequiredService<IQueryExecutor>();
                var store = provider.GetRequiredService<AiAgentRunStore>();
                var budget = provider.GetRequiredService<IAiOperationBudgetStore>();
                var repair = ActivatorUtilities.CreateInstance<AiAgentTerminalBudgetRepair>(provider);
                var scopeKey = tenantId?.ToString("N") ?? "host";
                if (tenantId.HasValue)
                    await commands.ExecuteAsync(new("test.ai.repair.quota", """
                        INSERT INTO fn_ai_tenant_quota
                            (Id,TenantId,MonthlyTokenLimit,MonthlyRequestLimit,UsedTokensThisMonth,UsedRequestsThisMonth,QuotaMonthKey,IsEnabled,CreatedAtUtc,UpdatedAtUtc,Version)
                        VALUES (@Id,@TenantId,100000,100,0,0,@Month,1,@Now,@Now,1)
                        """, SqlDataScope.Global), AiSqlParameters.Create(("Id", Guid.CreateVersion7()), ("TenantId", tenantId),
                            ("Month", DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)), ("Now", DateTimeOffset.UtcNow)));

                foreach (var scenario in new[] { "single", "loop", "completed", "failed", "cancelled", "authorization_required", "expired",
                    "corrupt", "stale", "rollback", "wrong-scope", "known", "unknown", "concurrent", "active",
                    "not-dispatched-completed", "not-dispatched-failed" })
                {
                    var definition = scenario == "loop" ? AgentDefinitionRegistry.ReadOnlyToolLoopKey : AgentDefinitionRegistry.SingleTextKey;
                    var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, definition, tenantId);
                    Bind();
                    var run = (await store.FindByIdAsync(runId))!;
                    var modelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
                    await budget.ReserveAsync(new(runId, runId, modelId, "agent", new('E', 64), 100, 100, ProviderKey: "ollama", ModelId: "model"));
                    var proved = scenario is "single" or "loop" or "rollback" or "concurrent";
                    var zeroProved = scenario == "not-dispatched-failed";
                    if (proved || scenario == "corrupt" || scenario.StartsWith("not-dispatched", StringComparison.Ordinal))
                    {
                        var lease = (await store.TryAcquireAsync(runId, "repair-history", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                        var state = AiModelRunCheckpoint.Start(run, new() { Id = modelId, Version = 1 }, "");
                        var payload = (scenario.StartsWith("not-dispatched", StringComparison.Ordinal)
                            ? state with { Phase = "not_dispatched" }
                            : state.Complete(new("done", JsonSerializer.SerializeToElement(true), 2, 1))).ToJson();
                        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
                            new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload,
                                scenario == "corrupt" ? new('0', 64) : AiModelRunCheckpoint.Digest(payload)),
                            new(Guid.CreateVersion7(), 1, scenario.StartsWith("not-dispatched", StringComparison.Ordinal) ? "run.failed" : "run.node_completed", 1, "{}"))));
                    }
                    var status = zeroProved ? "failed" : scenario is "failed" or "cancelled" or "authorization_required" or "expired" ? scenario : scenario == "active" ? "running" : "completed";
                    await commands.ExecuteAsync(new("test.ai.repair.historical_terminal", """
                        UPDATE fn_ai_agent_run SET StatusKey=@Status, Version=Version+1, LeaseOwner=NULL, LeaseExpiresAtUtc=NULL,
                            DeadlineAtUtc=@Expired WHERE Id=@RunId
                        """, SqlDataScope.Global), AiSqlParameters.Create(("RunId", runId), ("Status", status), ("Expired", DateTimeOffset.UtcNow.AddDays(-1))));
                    if (scenario == "known") await budget.SettleAsync(runId, new(7, 3), "succeeded");
                    if (scenario == "unknown") await budget.SettleAsync(runId, new(null, null), "succeeded");
                    run = (await store.FindByIdAsync(runId))!;
                    var quotaBefore = await ReadQuotaAsync();
                    var before = (await ReadBudgetAsync())!;
                    var candidates = await store.ScanTerminalUnsettledAsync(100, CancellationToken.None);
                    Assert.AreEqual(scenario is not ("known" or "unknown" or "active"), candidates.Contains(runId), scenario);

                    if (scenario is "known" or "unknown" or "active")
                    {
                        Assert.IsFalse(await repair.RepairAsync(runId, CancellationToken.None), scenario);
                        Assert.AreEqual(run.Version, (await store.FindByIdAsync(runId))!.Version, scenario);
                    }
                    else
                    {
                        if (scenario == "rollback")
                        {
                            // 真实预算与兼容配额已经更新后制造物理提交前故障，重试必须只收敛一次。
                            await Assert.ThrowsAsync<IOException>(() => provider.GetRequiredService<ICommandTransaction>().ExecuteAsync<bool>(async token =>
                            {
                                Assert.IsTrue(await repair.RepairAsync(runId, token));
                                throw new IOException("terminal repair outer commit failed");
                            }, CancellationToken.None));
                            Assert.AreEqual(run.Version, (await store.FindByIdAsync(runId))!.Version);
                            Assert.AreEqual("reserved", (await ReadBudgetAsync())!.UsageStatus);
                            Assert.AreEqual(quotaBefore, await ReadQuotaAsync());
                        }
                        if (scenario == "wrong-scope")
                        {
                            context.SetTenant(new(Guid.CreateVersion7(), "other", "其他租户"));
                            Assert.IsFalse(await repair.RepairAsync(runId, CancellationToken.None));
                            Bind();
                            Assert.AreEqual("reserved", (await ReadBudgetAsync())!.UsageStatus);
                            Assert.AreEqual(run.Version, (await store.FindByIdAsync(runId))!.Version);
                        }
                        if (scenario == "stale")
                        {
                            await commands.ExecuteAsync(new("test.ai.repair.version", "UPDATE fn_ai_agent_run SET Version=Version+1 WHERE Id=@RunId", SqlDataScope.Global), AiSqlParameters.Create(("RunId", runId)));
                            Assert.AreEqual(0, await store.ClaimTerminalBudgetRepairAsync(run, before, CancellationToken.None));
                            Assert.AreEqual("reserved", (await ReadBudgetAsync())!.UsageStatus);
                            run = (await store.FindByIdAsync(runId))!;
                        }
                        if (scenario == "concurrent")
                        {
                            var results = await Task.WhenAll(RepairInSeparateScopeAsync(), RepairInSeparateScopeAsync());
                            Assert.AreEqual(1, results.Count(r => r));
                        }
                        else Assert.IsTrue(await repair.RepairAsync(runId, CancellationToken.None), scenario);
                        Assert.IsFalse(await repair.RepairAsync(runId, CancellationToken.None), scenario);
                        Assert.AreEqual(run.Version + 1, (await store.FindByIdAsync(runId))!.Version, scenario);
                        Assert.IsFalse((await store.ScanTerminalUnsettledAsync(100, CancellationToken.None)).Contains(runId));
                    }
                    Assert.AreEqual(status, (await store.FindByIdAsync(runId))!.StatusKey, scenario);
                    Assert.IsNull(await store.TryAcquireAsync(runId, "repair-must-not-reopen", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
                    var settled = (await ReadBudgetAsync())!;
                    Assert.AreEqual(before.QuotaMonthKey, settled.QuotaMonthKey);
                    Assert.AreEqual(before.ReservedTokens, settled.ReservedTokens);
                    Assert.AreEqual(before.PriceVersionId, settled.PriceVersionId);
                    Assert.AreEqual(proved || zeroProved || scenario == "known" ? "known" : scenario == "active" ? "reserved" : "unknown", settled.UsageStatus, scenario);
                    Assert.AreEqual(scenario == "known" ? 7L : proved ? 2L : zeroProved ? 0L : null, settled.InputTokens, scenario);
                    Assert.AreEqual(scenario == "active" ? "pending" : status == "completed" ? "succeeded" : status == "failed" ? "failed" : "cancelled", settled.Outcome, scenario);
                    if (tenantId.HasValue) Assert.AreEqual(quotaBefore - (proved ? 197 : zeroProved ? 200 : 0), await ReadQuotaAsync(), scenario);

                    Task<AiOperationBudgetRecord?> ReadBudgetAsync() => queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(
                        AiOperationBudgetSql.Find, AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", scopeKey)));
                    Task<long> ReadQuotaAsync() => tenantId.HasValue ? queries.QuerySingleOrDefaultAsync<long>(
                        new("test.ai.repair.quota_tokens", "SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@TenantId", SqlDataScope.Global), AiSqlParameters.Create(("TenantId", tenantId))) : Task.FromResult(0L);
                    async Task<bool> RepairInSeparateScopeAsync()
                    {
                        await using var isolated = factory.Services.CreateAsyncScope();
                        var writer = isolated.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                        if (tenantId is { } id) writer.SetTenant(new(id, "concurrent-repair", "并发修复")); else writer.SetHost();
                        try { return await ActivatorUtilities.CreateInstance<AiAgentTerminalBudgetRepair>(isolated.ServiceProvider).RepairAsync(runId, CancellationToken.None); }
                        finally { writer.Clear(); }
                    }
                }

                // 满批坏绑定必须保持原账本，同时跨 Scoped 轮询传递游标，让后续有效记录最终收敛。
                var poison = new List<Guid>();
                var validId = Guid.Empty;
                for (var index = 0; index < 5; index++)
                {
                    var id = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId);
                    Bind();
                    await budget.ReserveAsync(new(id, id, Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        "agent", new('D', 64), 100, 100, ProviderKey: "ollama", ModelId: "model"));
                    await commands.ExecuteAsync(new("test.ai.repair.poisoned_scope", """
                        UPDATE fn_ai_agent_run SET StatusKey='completed', TenantId=@TenantId,
                            UpdatedAtUtc=@Old WHERE Id=@RunId
                        """, SqlDataScope.Global), AiSqlParameters.Create(("RunId", id),
                        ("TenantId", index < 4 ? Guid.CreateVersion7() : tenantId), ("Old", DateTimeOffset.UtcNow.AddDays(-2))));
                    if (index < 4) poison.Add(id); else validId = id;
                }
                Guid? cursor = null;
                var visited = new HashSet<Guid>();
                for (var pass = 0; pass < 100; pass++)
                {
                    var processor = new AiAgentTerminalBudgetRepairProcessor(
                        new TerminalRepairScopeFactory(factory.Services.GetRequiredService<IServiceScopeFactory>()), store,
                        Options.Create(new AiAgentRuntimeOptions { BatchSize = 1 }),
                        provider.GetRequiredService<ILogger<AiAgentTerminalBudgetRepairProcessor>>());
                    await processor.ProcessPendingAsync(CancellationToken.None, cursor);
                    cursor = processor.LastScannedRunId;
                    if (cursor is null) break;
                    Assert.IsTrue(visited.Add(cursor.Value), "扫描同一轮不得重复停在坏行");
                }
                Assert.IsNull(cursor, "有界数据应到达尾部并重置游标");
                Assert.IsTrue(poison.All(visited.Contains));
                Assert.IsTrue(visited.Contains(validId));
                Assert.AreEqual("unknown", (await queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
                    AiSqlParameters.Create(("OperationId", validId), ("ScopeKey", scopeKey))))!.UsageStatus);
                foreach (var id in poison)
                    Assert.AreEqual("reserved", (await queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
                        AiSqlParameters.Create(("OperationId", id), ("ScopeKey", scopeKey))))!.UsageStatus);
                Assert.IsTrue((await store.ScanTerminalUnsettledAsync(100, CancellationToken.None)).Any(poison.Contains), "重置后允许重试历史坏行");
            }
            finally { context.Clear(); }
        }
    }

    // API 夹具不注册 Worker 处理器；适配其作用域只补测试所需修复服务，仍使用真实 DB 和租户上下文。
    private sealed class TerminalRepairScopeFactory(IServiceScopeFactory source) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TerminalRepairScope(source.CreateScope());
    }

    private sealed class TerminalRepairScope(IServiceScope inner) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new TerminalRepairServices(inner.ServiceProvider);
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync()
        {
            if (inner is IAsyncDisposable disposable) return disposable.DisposeAsync();
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TerminalRepairServices(IServiceProvider inner) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(AiAgentTerminalBudgetRepair)
            ? ActivatorUtilities.CreateInstance<AiAgentTerminalBudgetRepair>(inner) : inner.GetService(type);
    }
}
