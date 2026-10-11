using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>未派发拒绝与会话失效收尾不能在状态已提交后单独结算。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow("session")] [DataRow("model")] [DataRow("single")] [DataRow("workflow")]
    public async Task Rejection_settles_inside_same_transaction_as_status_event(string scenario)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await RejectionFixtureAsync(scenario, transactions);
        RecordingDbTransactionCoordinator? eventTransaction = null;
        fixture.Commands.When(x => x.ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var type = (string)((IReadOnlyDictionary<string, object?>)call[1]!)["EventType"]!;
                if (type is "run.failed" or "run.authorization_required")
                    eventTransaction = transactions.SingleOrDefault(t => t.HasTransaction);
            });
        var activeAtSettlement = false;
        fixture.Budget.When(x => x.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => activeAtSettlement = eventTransaction?.HasTransaction == true);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        Assert.IsTrue(activeAtSettlement, "拒绝事件与预算必须使用同一未提交事务。");
        Assert.IsNotNull(eventTransaction);
        Assert.AreEqual(1, eventTransaction.CommitCount);
        Assert.AreEqual(0, eventTransaction.RollbackCount);
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(),
            scenario == "session" ? "cancelled" : "failed", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("session")] [DataRow("model")] [DataRow("single")] [DataRow("workflow")]
    public async Task Rejection_settlement_failure_rolls_back_without_second_settlement(string scenario)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await RejectionFixtureAsync(scenario, transactions);
        fixture.Budget.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiBudgetException("ai.budget.settlement_conflict"));
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("session")] [DataRow("model")] [DataRow("single")] [DataRow("workflow")]
    public async Task Rejection_cancellation_returned_by_settlement_rolls_back(string scenario)
    {
        using var cancellation = new CancellationTokenSource();
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await RejectionFixtureAsync(scenario, transactions);
        fixture.Budget.When(x => x.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => cancellation.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.ProcessPendingAsync(cancellation.Token));
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("session")] [DataRow("model")] [DataRow("single")] [DataRow("workflow")]
    public async Task Rejection_event_failure_rolls_back_without_settlement(string scenario)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await RejectionFixtureAsync(scenario, transactions);
        fixture.Commands.ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => (string)((IReadOnlyDictionary<string, object?>)call[1]!)["EventType"]! is "run.failed" or "run.authorization_required"
                ? throw new IOException("rejection event failed") : 1);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("session")] [DataRow("model")] [DataRow("single")] [DataRow("workflow")]
    public async Task Rejection_lost_lease_never_settles(string scenario)
    {
        var fixture = await RejectionFixtureAsync(scenario, []);
        fixture.Commands.ExecuteAsync(AiAgentRunSql.UpdateRunStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => (string)((IReadOnlyDictionary<string, object?>)call[1]!)["StatusKey"]! is "failed" or "authorization_required" ? 0 : 1);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static Task<CoordinatorFixture> RejectionFixtureAsync(string scenario, List<RecordingDbTransactionCoordinator> transactions)
    {
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<AgentModelResult>>(_ => throw new AiRunOutputBudgetException(false));
        return CreateFixtureAsync(bindingValid: scenario != "session", includeModel: scenario != "model",
            definitionKey: scenario == "workflow" ? AgentWorkflowRegistry.ChatRenameWorkflowKey : AgentDefinitionRegistry.SingleTextKey,
            runner: runner, transactionCreated: transactions.Add);
    }
}
