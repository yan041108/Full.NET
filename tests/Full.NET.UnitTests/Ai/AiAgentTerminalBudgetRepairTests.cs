using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>历史终态只修复账本；凭证不足保留未知预留，状态与结算同事务保护。</summary>
[TestClass]
public sealed class AiAgentTerminalBudgetRepairTests
{
    [TestMethod]
    [DataRow("completed", "succeeded")]
    [DataRow("failed", "failed")]
    [DataRow("authorization_required", "cancelled")]
    [DataRow("cancelled", "cancelled")]
    [DataRow("expired", "cancelled")]
    public async Task Terminal_repair_without_receipt_preserves_unknown_usage(string status, string outcome)
    {
        var f = Create(); f.Run.StatusKey = status;
        Assert.IsTrue(await RepairAsync(f));
        await f.Budget.Received(1).SettleAsync(f.Run.Id,
            Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == null && u.OutputTokens == null), outcome, Arg.Any<CancellationToken>());
        Assert.AreEqual(status, f.Run.StatusKey);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("queued")]
    [DataRow("running")]
    [DataRow("awaiting_approval")]
    [DataRow("reconciliation_required")]
    [DataRow("retry_scheduled")]
    public async Task Terminal_repair_never_settles_active_or_paused_run(string status)
    {
        var f = Create(); f.Run.StatusKey = status;
        Assert.IsFalse(await RepairAsync(f));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)]
    public async Task Terminal_repair_uses_original_completed_receipt_without_execution(string definition)
    {
        var f = Create(); f.Run.DefinitionKey = definition; Receipt(f, "completed");
        Assert.IsTrue(await RepairAsync(f));
        await f.Budget.Received(1).SettleAsync(f.Run.Id, new(2, 1), "succeeded", Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("started")]
    [DataRow("checksum")]
    [DataRow("model")]
    [DataRow("input")]
    [DataRow("budget")]
    [DataRow("reservation")]
    [DataRow("partial")]
    [DataRow("legacy")]
    public async Task Terminal_repair_does_not_infer_known_usage_from_incomplete_proof(string scenario)
    {
        var f = Create(); var receipt = Receipt(f, scenario == "started" ? "started" : "completed", scenario == "partial");
        if (scenario == "checksum") SetCheckpoint(f, receipt with { Checksum = new('0', 64) });
        if (scenario == "model") SetOperation(f, modelId: Guid.CreateVersion7());
        if (scenario == "input") f.Run.BudgetJson = JsonSerializer.Serialize(new AgentRunBudgetSnapshot(f.ModelId, "changed", 100, 100), AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
        if (scenario == "budget") f.Run.BudgetJson = "invalid";
        if (scenario == "reservation") SetOperation(f, tokens: 201);
        if (scenario == "legacy") SetCheckpoint(f, receipt with { PayloadProtected = "{\"messages\":[]}" });
        Assert.IsTrue(await RepairAsync(f));
        await f.Budget.Received(1).SettleAsync(f.Run.Id,
            Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == null && u.OutputTokens == null), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Terminal_repair_confirmed_single_not_dispatched_releases_zero_usage()
    {
        var f = Create(); f.Run.StatusKey = "failed"; Receipt(f, "not_dispatched");
        Assert.IsTrue(await RepairAsync(f));
        await f.Budget.Received(1).SettleAsync(f.Run.Id, new(0, 0), "failed", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("cancelled")]
    [DataRow("authorization_required")]
    [DataRow("expired")]
    public async Task Terminal_repair_contradictory_not_dispatched_preserves_unknown(string status)
    {
        var f = Create(); f.Run.StatusKey = status; Receipt(f, "not_dispatched");
        Assert.IsTrue(await RepairAsync(f));
        await f.Budget.Received(1).SettleAsync(f.Run.Id,
            Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == null && u.OutputTokens == null),
            status == "completed" ? "succeeded" : "cancelled", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Terminal_scan_rotates_past_unrepairable_rows(bool mysql)
    {
        var sql = mysql ? AiAgentRunSql.ScanTerminalBudgetMySql : AiAgentRunSql.ScanTerminalBudgetSqlServer;
        // 错误历史绑定会原样保留，扫描必须支持越过已检查行，否则满批坏行阻断后续有效预留。
        StringAssert.Contains(sql.Text, "r.Id > @AfterRunId");
        StringAssert.Contains(sql.Text, "ORDER BY r.Id");
    }

    [TestMethod]
    [DataRow("known")]
    [DataRow("unknown")]
    public async Task Terminal_repair_leaves_existing_settlement_untouched(string status)
    {
        var f = Create(); SetOperation(f, status: status);
        Assert.IsFalse(await RepairAsync(f));
        Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("tenant")]
    [DataRow("unbound")]
    public async Task Terminal_repair_rejects_untrusted_or_drifted_scope(string scenario)
    {
        var f = Create();
        if (scenario == "scope") f.Run.ScopeKey = Guid.CreateVersion7().ToString("N");
        if (scenario == "tenant") f.Run.TenantId = Guid.CreateVersion7();
        if (scenario == "unbound") f.Tenant.Clear();
        Assert.IsFalse(await RepairAsync(f));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Terminal_repair_uses_matching_trusted_tenant()
    {
        var f = Create(); var id = Guid.CreateVersion7(); f.Run.TenantId = id; f.Run.ScopeKey = id.ToString("N");
        f.Tenant.SetTenant(new(id, "repair", "预算修复"));
        Assert.IsTrue(await RepairAsync(f));
        var find = f.Queries.ReceivedCalls().Single(c => c.GetArguments()[0] is SqlStatement s && s == AiOperationBudgetSql.Find);
        Assert.AreEqual(id.ToString("N"), ((IReadOnlyDictionary<string, object?>)find.GetArguments()[1]!)["ScopeKey"]);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Terminal_repair_requires_matching_reserved_operation(bool missing)
    {
        var f = Create();
        if (missing) f.Queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((AiOperationBudgetRecord?)null);
        else SetOperation(f, runId: Guid.CreateVersion7());
        Assert.IsFalse(await RepairAsync(f));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Terminal_repair_lost_version_never_settles()
    {
        var f = Create(); f.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse(await RepairAsync(f));
        Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Terminal_repair_settlement_failure_rolls_back_claim()
    {
        var f = Create();
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("budget unavailable"));
        await Assert.ThrowsAsync<IOException>(() => RepairAsync(f));
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Terminal_repair_late_cancellation_rolls_back_claim_and_settlement()
    {
        var f = Create(); using var cancel = new CancellationTokenSource();
        f.Budget.When(b => b.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(_ => cancel.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => RepairAsync(f, cancel.Token));
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Terminal_repair_late_scope_change_rolls_back()
    {
        var f = Create();
        f.Budget.When(b => b.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(_ => f.Tenant.Clear());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RepairAsync(f));
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    private static async Task<bool> RepairAsync(Fixture f, CancellationToken token = default)
    {
        var repair = new AiAgentTerminalBudgetRepair(f.Store, f.Queries, f.Transaction, f.Budget, f.Tenant);
        return await repair.RepairAsync(f.Run.Id, token);
    }

    private static Fixture Create()
    {
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var coordinator = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(coordinator);
        var modelId = Guid.CreateVersion7();
        var run = new AiAgentRunRecord { Id = Guid.CreateVersion7(), ScopeKey = "host", SessionId = Guid.CreateVersion7(),
            DefinitionKey = AgentDefinitionRegistry.SingleTextKey, DefinitionVersion = 1, StatusKey = "completed", Version = 8,
            BudgetJson = JsonSerializer.Serialize(new AgentRunBudgetSnapshot(modelId, "hello", 100, 100), AiJsonSerializerContext.Default.AgentRunBudgetSnapshot) };
        queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(AiAgentRunSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(run);
        var f = new Fixture(new(queries, commands, transaction, ids, clock, Options.Create(new DatabaseOptions())), queries, commands,
            transaction, coordinator, Substitute.For<IAiOperationBudgetStore>(), tenant, run, modelId);
        SetOperation(f); return f;
    }

    private static void SetOperation(Fixture f, string status = "reserved", Guid? runId = null, Guid? modelId = null, long tokens = 200) =>
        f.Queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationBudgetRecord { Id = f.Run.Id, RunId = runId ?? f.Run.Id, ModelConfigId = modelId ?? f.ModelId,
                RequestHash = new('A', 64), ReservedTokens = tokens, UsageStatus = status, Outcome = "pending" });

    private static AgentCheckpointRecord Receipt(Fixture f, string phase, bool partial = false)
    {
        var state = AiModelRunCheckpoint.Start(f.Run, new() { Id = f.ModelId, Version = 1 }, "hello");
        state = phase == "completed" ? state.Complete(new("done", JsonSerializer.SerializeToElement(true), partial ? null : 2, 1)) : state with { Phase = phase };
        var payload = state.ToJson();
        var receipt = new AgentCheckpointRecord(Guid.CreateVersion7(), f.Run.Id, 2, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload, AiModelRunCheckpoint.Digest(payload));
        SetCheckpoint(f, receipt); return receipt;
    }

    private static void SetCheckpoint(Fixture f, AgentCheckpointRecord receipt) =>
        f.Queries.QuerySingleOrDefaultAsync<AgentCheckpointRecord>(AiAgentRunSql.FindLatestCheckpoint, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(receipt);

    private sealed record Fixture(AiAgentRunStore Store, IQueryExecutor Queries, ICommandExecutor Commands, DapperCommandTransaction Transaction,
        RecordingDbTransactionCoordinator Coordinator, IAiOperationBudgetStore Budget, CurrentTenantAccessor Tenant, AiAgentRunRecord Run, Guid ModelId);
}
