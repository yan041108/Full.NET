using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow("session", false)] [DataRow("session", true)]
    [DataRow("model", false)] [DataRow("model", true)]
    [DataRow("runner", false)] [DataRow("runner", true)]
    [DataRow("definition", false)] [DataRow("definition", true)]
    [DataRow("checkpoint", false)] [DataRow("checkpoint", true)]
    [DataRow("payload", false)] [DataRow("payload", true)]
    [DataRow("prompt", false)] [DataRow("prompt", true)]
    [DataRow("prompt-after-factory", false)] [DataRow("prompt-after-factory", true)]
    [DataRow("workflow", false)] [DataRow("workflow", true)]
    [DataRow("tool-budget", false)] [DataRow("tool-budget", true)]
    public async Task Rejection_settles_unknown_budget_only_after_fenced_status_commit(string failure, bool ownsLease)
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("loop", "ai.tools.ping", new Dictionary<string, object?>())])));
        var fixture = await CreateFixtureAsync(
            bindingValid: failure != "session", includeModel: failure != "model", runnerThrows: failure == "runner",
            commitSucceeds: ownsLease,
            definitionKey: failure is "workflow" or "checkpoint" ? AgentWorkflowRegistry.ChatRenameWorkflowKey
                : failure == "tool-budget" ? AgentDefinitionRegistry.ReadOnlyToolLoopKey : AiAgentRunManagementService.SingleTextDefinitionKey,
            chatClient: client,
            promptBound: failure.StartsWith("prompt", StringComparison.Ordinal),
            promptDeactivated: failure == "prompt", deactivateDuringFactory: failure == "prompt-after-factory",
            checkpointFactory: failure == "checkpoint" ? run => FaultedCheckpoint(run, "checksum") : null,
            configureRecord: run => { if (failure == "definition") run.DefinitionVersion = 999; if (failure == "payload") run.BudgetJson = "null"; });
        var processed = await fixture.Coordinator.ProcessPendingAsync();
        if (ownsLease)
            await fixture.Budget.Received(1).SettleAsync(fixture.RunId,
                Arg.Is<AiOperationUsage>(usage => usage != null && usage.InputTokens == null && usage.OutputTokens == null),
                failure == "session" ? "cancelled" : "failed", Arg.Any<CancellationToken>());
        else
            await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(ownsLease ? 1 : 0, processed);
        AssertStatusCommit(fixture.Commands, failure == "session" ? "authorization_required" : "failed");
    }

    [TestMethod]
    [DataRow(AiAgentRunManagementService.SingleTextDefinitionKey, true)]
    [DataRow(AiAgentRunManagementService.SingleTextDefinitionKey, false)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, true)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, false)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, true)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, false)]
    public async Task Cancellation_during_model_lookup_never_creates_client_or_records_failure(string definitionKey, bool includeModel)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = await CreateFixtureAsync(definitionKey: definitionKey, includeModel: includeModel, duringModelLookup: cancellation.Cancel);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Coordinator.ProcessPendingAsync(cancellation.Token));
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        AssertNoStatusOrSettlement(fixture);
    }

    [TestMethod]
    [DataRow(AiAgentRunManagementService.SingleTextDefinitionKey, false)]
    [DataRow(AiAgentRunManagementService.SingleTextDefinitionKey, true)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, false)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, true)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, false)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, true)]
    public async Task Cancellation_during_client_creation_disposes_returned_client_without_dispatch_or_status_commit(string definitionKey, bool factoryThrows)
    {
        using var cancellation = new CancellationTokenSource();
        var client = Substitute.For<IChatClient>();
        var fixture = await CreateFixtureAsync(definitionKey: definitionKey);
        fixture.Factory.CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); if (factoryThrows) throw new InvalidOperationException("late provider failure"); return client; });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Coordinator.ProcessPendingAsync(cancellation.Token));
        client.Received(factoryThrows ? 0 : 1).Dispose();
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        AssertNoStatusOrSettlement(fixture);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_during_single_text_return_does_not_commit_success_or_settle(bool promptBound)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return new AgentModelResult("late", JsonSerializer.SerializeToElement(new { messages = Array.Empty<string>() }), 2, 1); });
        var fixture = await CreateFixtureAsync(runner: runner, promptBound: promptBound);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Coordinator.ProcessPendingAsync(cancellation.Token));
        await fixture.Runner.Received(1).RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        AssertNoStatusOrSettlement(fixture);
    }

    private static void AssertNoStatusOrSettlement(CoordinatorFixture fixture)
    {
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any(call => call.GetArguments()[0] is Full.NET.Data.Abstractions.SqlStatement statement
            && statement == Full.NET.Modules.Ai.Persistence.AiAgentRunSql.UpdateRunStatus));
        Assert.IsFalse(fixture.Budget.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IAiOperationBudgetStore.SettleAsync)));
    }
}
