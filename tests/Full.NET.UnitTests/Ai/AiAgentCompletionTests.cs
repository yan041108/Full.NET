using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey)]
    public async Task Successful_completion_settles_inside_same_transaction_as_terminal_event(string definition)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await CompletionFixtureAsync(definition, transactions);
        RecordingDbTransactionCoordinator? eventTransaction = null;
        fixture.Commands.When(x => x.ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var values = (IReadOnlyDictionary<string, object?>)call[1]!;
                if ((string)values["EventType"]! == "run.completed")
                    eventTransaction = transactions.SingleOrDefault(t => t.HasTransaction);
            });
        var activeAtSettlement = false;
        fixture.Budget.When(x => x.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>()))
            .Do(_ => activeAtSettlement = eventTransaction?.HasTransaction == true);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        Assert.IsTrue(activeAtSettlement, "成功事件与预算结算必须处于同一个未提交事务。");
        Assert.IsNotNull(eventTransaction);
        Assert.AreEqual(1, eventTransaction.CommitCount);
        Assert.AreEqual(0, eventTransaction.RollbackCount);
    }

    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey)]
    public async Task Completion_settlement_failure_rolls_back_without_failed_reclassification(string definition)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await CompletionFixtureAsync(definition, transactions);
        fixture.Budget.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiBudgetException("ai.budget.persistence_failed"));
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Budget.DidNotReceive().SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any(c => c.GetArguments()[0] is SqlStatement s
            && s == AiAgentRunSql.UpdateRunStatus && (string)((IReadOnlyDictionary<string, object?>)c.GetArguments()[1]!)["StatusKey"]! == "failed"));
    }

    private static Task<CoordinatorFixture> CompletionFixtureAsync(string definition,
        List<RecordingDbTransactionCoordinator> transactions)
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")) { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } });
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new AgentModelResult(call.ArgAt<string>(1).Contains("decision", StringComparison.Ordinal)
                ? "{\"decision\":\"approved\"}" : "Title", JsonSerializer.SerializeToElement(true), 2, 1));
        return CreateFixtureAsync(definitionKey: definition, chatClient: client, runner: runner, transactionCreated: transactions.Add);
    }

    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey)]
    public async Task Cancellation_returned_by_settlement_rolls_back_completion(string definition)
    {
        using var cancellation = new CancellationTokenSource();
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await CompletionFixtureAsync(definition, transactions);
        fixture.Budget.When(x => x.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>()))
            .Do(_ => cancellation.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.ProcessPendingAsync(cancellation.Token));
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Budget.DidNotReceive().SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey)]
    public async Task Final_completion_lost_lease_never_settles(string definition)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await CompletionFixtureAsync(definition, transactions);
        fixture.Commands.ExecuteAsync(AiAgentRunSql.UpdateRunStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => (string)((IReadOnlyDictionary<string, object?>)call[1]!)["StatusKey"]! == "completed" ? 0 : 1);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(AgentDefinitionRegistry.SingleTextKey)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey)]
    public async Task Completion_event_failure_rolls_back_without_settlement_or_reclassification(string definition)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await CompletionFixtureAsync(definition, transactions);
        fixture.Commands.ExecuteAsync(AiAgentRunSql.InsertEvent, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => (string)((IReadOnlyDictionary<string, object?>)call[1]!)["EventType"]! == "run.completed"
                ? throw new IOException("completion event failed") : 1);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Restored_single_completion_failure_preserves_original_receipt_without_dispatch(bool unknown)
    {
        var transactions = new List<RecordingDbTransactionCoordinator>();
        var fixture = await CreateFixtureAsync(checkpointFactory: run => CompletedSingleCheckpoint(run, unknown ? null : 2, 1),
            transactionCreated: transactions.Add);
        fixture.Budget.SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("settlement unavailable"));
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(1, transactions.Sum(t => t.RollbackCount));
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<Full.NET.AI.Abstractions.Models.ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
    }
}
