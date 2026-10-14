using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工对账关闭未知结果，原预算结算和证据须原子写入，永不重新调用模型或工具。</summary>
[TestClass]
public sealed class AiAgentReconciliationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Reconciliation_records_original_usage_or_preserves_unknown_and_commits_once(bool known)
    {
        var f = Create(); var request = Request(f, known);
        Assert.IsTrue((await f.Service.ReconcileAsync(f.Run.Id, request, default)).IsSuccess);
        await f.Budget.Received(1).SettleAsync(f.Run.Id, new(known ? 10 : null, known ? 2 : null, known ? 3 : null), "failed", Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.CommitCount); Assert.AreEqual(0, f.Coordinator.RollbackCount);
        await f.Commands.Received(1).ExecuteAsync(AiAgentRunSql.ReconcileOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(2).ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(f.Commands.ReceivedCalls().All(call => call.GetArguments()[0] is not SqlStatement sql || sql != AiAgentRunSql.ResumeOwned));
    }

    [TestMethod]
    [DataRow("queued")] [DataRow("running")] [DataRow("completed")] [DataRow("awaiting_approval")]
    public async Task Other_states_cannot_be_reconciled_without_original_evidence(string status)
    {
        var f = Create(); f.Run.StatusKey = status;
        Assert.IsFalse((await f.Service.ReconcileAsync(f.Run.Id, Request(f), default)).IsSuccess);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray()); Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow("permission")] [DataRow("owner")] [DataRow("scope")] [DataRow("version")] [DataRow("model")] [DataRow("reservation")]
    public async Task Mismatched_authority_or_original_budget_refuses_before_writes(string mismatch)
    {
        var f = Create();
        if (mismatch == "permission") f.Authorization.AuthorizeAsync(AiAgentRunPermissions.Reconcile, Arg.Any<CancellationToken>()).Returns((AuthorizedSessionActor?)null);
        if (mismatch == "owner") f.Queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(AiAgentRunSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((AiAgentRunRecord?)null);
        if (mismatch == "scope") f.Tenant.Clear();
        if (mismatch is "model" or "reservation") f.Queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationBudgetRecord { Id = f.Run.Id, RunId = f.Run.Id, ModelConfigId = mismatch == "model" ? Guid.CreateVersion7() : f.ModelId,
                ReservedTokens = mismatch == "reservation" ? 1 : 200, UsageStatus = "unknown" });
        var request = Request(f) with { ExpectedVersion = mismatch == "version" ? 7 : 8 };
        Assert.IsFalse((await f.Service.ReconcileAsync(f.Run.Id, request, default)).IsSuccess);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray()); Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Lost_version_claim_never_settles_or_emits_event()
    {
        var f = Create(); f.Commands.ExecuteAsync(AiAgentRunSql.ReconcileOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Service.ReconcileAsync(f.Run.Id, Request(f), default)).IsSuccess);
        Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
        await f.Commands.DidNotReceive().ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_independent_capacity_refuses_before_any_transaction_or_business_write()
    {
        var f = Create(); f.Independent.HasIndependentReadCapacity.Returns(false);
        await Assert.ThrowsExactlyAsync<Full.NET.Abstractions.Results.ServiceCapacityExceededException>(() => f.Service.ReconcileAsync(f.Run.Id, Request(f), default));
        Assert.AreEqual(0, f.Coordinator.BeginCount); Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Known_official_usage_is_preserved_when_closing_unknown_external_execution()
    {
        var f = Create(); f.Queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationBudgetRecord { Id = f.Run.Id, RunId = f.Run.Id, ModelConfigId = f.ModelId, ReservedTokens = 200,
                UsageStatus = "known", Outcome = "succeeded", InputTokens = 10, OutputTokens = 2, CachedInputTokens = 3 });
        Assert.IsFalse((await f.Service.ReconcileAsync(f.Run.Id, Request(f, true) with { InputTokens = 1 }, default)).IsSuccess);
        Assert.IsTrue((await f.Service.ReconcileAsync(f.Run.Id, Request(f), default)).IsSuccess);
        await f.Budget.Received(1).SettleAsync(f.Run.Id, new(10, 2, 3), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_receipt_conflict_rolls_back_and_returns_conflict_result()
    {
        var f = Create(); f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiBudgetException("ai.budget.settlement_conflict"));
        Assert.AreEqual("ai.agent_reconciliation.conflict", (await f.Service.ReconcileAsync(f.Run.Id, Request(f, true), default)).Error!.Code);
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("settlement")] [DataRow("event")] [DataRow("revoked")] [DataRow("scope")]
    public async Task Failure_during_commit_rolls_back_all_business_writes(string failure)
    {
        var f = Create();
        if (failure == "settlement") f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("failed"));
        if (failure == "event") f.Commands.ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        if (failure == "revoked") f.Independent.AuthorizeAsync(AiAgentRunPermissions.Reconcile, Arg.Any<CancellationToken>()).Returns((AuthorizedSessionActor?)null);
        if (failure == "scope") f.Budget.When(x => x.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(_ => f.Tenant.Clear());
        if (failure is "revoked" or "scope")
            Assert.AreEqual("authorization.permission_denied", (await f.Service.ReconcileAsync(f.Run.Id, Request(f), default)).Error!.Code);
        else await Assert.ThrowsAsync<Exception>(async () => await f.Service.ReconcileAsync(f.Run.Id, Request(f), default));
        Assert.AreEqual(0, f.Coordinator.CommitCount); Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task Exact_evidence_replay_returns_success_without_writes_but_changed_receipt_conflicts()
    {
        var f = Create(); var request = Request(f); f.Run.StatusKey = "failed";
        f.Queries.QuerySingleOrDefaultAsync<string>(AiAgentRunSql.FindReconciliation, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.ReconcileAiAgentRunRequest));
        Assert.IsTrue((await f.Service.ReconcileAsync(f.Run.Id, request, default)).IsSuccess);
        Assert.IsFalse((await f.Service.ReconcileAsync(f.Run.Id, request with { InputTokens = 0, OutputTokens = 0 }, default)).IsSuccess);
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray()); Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public void Partial_negative_or_uncertified_evidence_shapes_are_rejected()
    {
        var request = new ReconcileAiAgentRunRequest(Guid.CreateVersion7(), 8, new('A', 64));
        foreach (var invalid in new[] { request with { InputTokens = 0 }, request with { OutputTokens = 0 },
            request with { InputTokens = -1, OutputTokens = 0 }, request with { InputTokens = 1, OutputTokens = 0, CachedInputTokens = 2 },
            request with { EvidenceDigest = new('a', 64) }, request with { ExpectedVersion = long.MaxValue },
            request with { InputTokens = AiExecutionBudget.MaximumTokens + 1, OutputTokens = 0 } })
            Assert.IsFalse(AiAgentReconciliationService.Valid(invalid));
    }

    private static ReconcileAiAgentRunRequest Request(Fixture f, bool known = false) => new(Guid.CreateVersion7(), f.Run.Version,
        new('A', 64), known ? 10 : null, known ? 2 : null, known ? 3 : null);
    private static Fixture Create()
    {
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var coordinator = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(coordinator);
        var actor = new AuthorizedSessionActor(Guid.CreateVersion7(), null, Guid.CreateVersion7());
        var authorization = Substitute.For<ICurrentSessionAuthorization>(); authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(actor);
        var independent = Substitute.For<IIndependentCurrentSessionAuthorization>(); independent.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(actor);
        independent.HasIndependentReadCapacity.Returns(true);
        var model = Guid.CreateVersion7(); var run = new AiAgentRunRecord { Id = Guid.CreateVersion7(), ActorUserId = actor.UserId,
            ScopeKey = "host", StatusKey = "reconciliation_required", Version = 8,
            BudgetJson = JsonSerializer.Serialize(new AgentRunBudgetSnapshot(model, "prompt", 100, 100), AiJsonSerializerContext.Default.AgentRunBudgetSnapshot) };
        queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(AiAgentRunSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(run);
        queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationBudgetRecord { Id = run.Id, RunId = run.Id, ModelConfigId = model, ReservedTokens = 200, UsageStatus = "unknown", QuotaMonthKey = "2026-09" });
        var store = new AiAgentRunStore(queries, commands, transaction, ids, clock, Options.Create(new DatabaseOptions()));
        var budget = Substitute.For<IAiOperationBudgetStore>();
        return new(new(store, queries, commands, transaction, budget, authorization, tenant, clock, ids, independent, Substitute.For<IDataTransactionState>()), run, actor, model,
            queries, commands, budget, authorization, tenant, coordinator, independent);
    }
    private sealed record Fixture(AiAgentReconciliationService Service, AiAgentRunRecord Run, AuthorizedSessionActor Actor, Guid ModelId,
        IQueryExecutor Queries, ICommandExecutor Commands, IAiOperationBudgetStore Budget, ICurrentSessionAuthorization Authorization,
        CurrentTenantAccessor Tenant, RecordingDbTransactionCoordinator Coordinator, IIndependentCurrentSessionAuthorization Independent);
}
