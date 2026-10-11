using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Input_budget_unknown_or_exhausted_restore_rejects_before_factory(bool unknown)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = State(run.SessionId, 2);
                state.InputTokens = unknown ? null : 100;
                state.OutputTokens = 1;
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<Full.NET.AI.Abstractions.Models.ModelBinding>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(100L, false)] [DataRow(null, false)] [DataRow(101L, true)] [DataRow(-1L, true)]
    public async Task Input_budget_workflow_stops_later_model_and_preserves_intent_classification(long? input, bool uncertain)
    {
        var client = InputClient(input);
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            chatClient: client, runner: CallingModelRunner());
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, uncertain ? "reconciliation_required" : "failed");
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        var inserts = fixture.Commands.ReceivedCalls().Where(call => call.GetArguments()[0] is Full.NET.Data.Abstractions.SqlStatement s && s == AiAgentRunSql.InsertCheckpoint).ToArray();
        var values = Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(inserts[^1].GetArguments()[1]);
        var state = AgentWorkflowState.FromJson(Assert.IsInstanceOfType<string>(values["PayloadProtected"]));
        Assert.AreEqual(uncertain, state!.PendingNode is not null);
        Assert.AreEqual(uncertain ? 1 : 2, state.NextNodeIndex);
        if (!uncertain) Assert.AreEqual(input, state.InputTokens);
    }

    [TestMethod]
    [DataRow(100L, 1)] [DataRow(null, 1)] [DataRow(101L, 0)]
    public async Task Input_budget_loop_never_dispatches_later_model_or_tools_after_overrun(long? input, int tools)
    {
        var client = InputClient(input, true);
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey, chatClient: client);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.Received(tools).ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Input_budget_resume_deducts_previous_model_usage_before_accepting_receipt()
    {
        var client = InputClient(11);
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            chatClient: client, runner: CallingModelRunner(), checkpointFactory: run =>
            {
                var state = State(run.SessionId, 2); state.InputTokens = 90; state.OutputTokens = 1; return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IChatClient InputClient(long? input, bool tool = false)
    {
        var client = Substitute.For<IChatClient>();
        var message = tool ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("input-call", "ai.tools.ping", new Dictionary<string, object?>())])
            : new ChatMessage(ChatRole.Assistant, "Title");
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(message) { Usage = new() { InputTokenCount = input, OutputTokenCount = 1 } });
        return client;
    }
}
