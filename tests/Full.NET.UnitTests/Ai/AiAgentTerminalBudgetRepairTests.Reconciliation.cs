using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentTerminalBudgetRepairTests
{
    [TestMethod]
    [DataRow("reserved")] [DataRow("unknown")]
    public async Task Automatic_reconciliation_only_settles_original_completed_usage(string status)
    {
        var f = Create(); f.Run.ActorUserId = Guid.CreateVersion7(); f.Run.StatusKey = "reconciliation_required";
        Receipt(f, "completed"); SetOperation(f, status);
        Assert.IsTrue(await AutoReconcileAsync(f));
        await f.Budget.Received(1).SettleAsync(f.Run.Id, new(2, 1), "failed", Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        await f.Commands.Received(1).ExecuteAsync(AiAgentRunSql.ReconcileOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(2).ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Budget.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("started")] [DataRow("not_dispatched")] [DataRow("partial")] [DataRow("checksum")] [DataRow("wrong_model")] [DataRow("no_receipt")]
    public async Task Automatic_reconciliation_never_infers_a_receipt_or_zero_usage(string reason)
    {
        var f = Create(); f.Run.ActorUserId = Guid.CreateVersion7(); f.Run.StatusKey = "reconciliation_required";
        if (reason != "no_receipt")
        {
            var receipt = Receipt(f, reason is "started" or "not_dispatched" ? reason : "completed", reason == "partial");
            if (reason == "checksum") SetCheckpoint(f, receipt with { Checksum = new('0', 64) });
        }
        if (reason == "wrong_model") SetOperation(f, modelId: Guid.CreateVersion7());
        Assert.IsFalse(await AutoReconcileAsync(f));
        Assert.HasCount(0, f.Commands.ReceivedCalls().ToArray()); Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Automatic_reconciliation_claim_conflict_does_not_settle_or_append_events()
    {
        var f = Create(); f.Run.ActorUserId = Guid.CreateVersion7(); f.Run.StatusKey = "reconciliation_required"; Receipt(f, "completed");
        f.Commands.ExecuteAsync(AiAgentRunSql.ReconcileOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse(await AutoReconcileAsync(f)); Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
        await f.Commands.DidNotReceive().ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Automatic_reconciliation_waiting_receipt_change_rolls_back_claim()
    {
        var f = Create(); f.Run.ActorUserId = Guid.CreateVersion7(); f.Run.StatusKey = "reconciliation_required"; var receipt = Receipt(f, "completed");
        f.Queries.QuerySingleOrDefaultAsync<Full.NET.Agents.Runtime.AgentCheckpointRecord>(AiAgentRunSql.FindLatestCheckpoint,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(receipt, receipt with { Checksum = new('0', 64) });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AutoReconcileAsync(f));
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.HasCount(0, f.Budget.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Automatic_reconciliation_settlement_failure_rolls_back_run_transition()
    {
        var f = Create(); f.Run.ActorUserId = Guid.CreateVersion7(); f.Run.StatusKey = "reconciliation_required"; Receipt(f, "completed");
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("fixture settlement failure"));
        await Assert.ThrowsExactlyAsync<IOException>(() => AutoReconcileAsync(f));
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Automatic_reconciliation_late_scope_change_rolls_back_run_and_budget()
    {
        var f = Create(); f.Run.ActorUserId = Guid.CreateVersion7(); f.Run.StatusKey = "reconciliation_required"; Receipt(f, "completed");
        f.Budget.When(b => b.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())).Do(_ => f.Tenant.Clear());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AutoReconcileAsync(f));
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    private static Task<bool> AutoReconcileAsync(Fixture f)
    {
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var service = new AiAgentCompletedReceiptReconciliation(f.Store, f.Queries, f.Commands, f.Transaction, f.Budget, f.Tenant,
            clock, ids, Substitute.For<IDataTransactionState>());
        return service.ReconcileAsync(f.Run.Id, default);
    }
}
