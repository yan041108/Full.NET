using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow("output-low")] [DataRow("output-high")] [DataRow("output-unknown")]
    [DataRow("input-low")] [DataRow("input-unknown")] [DataRow("sum-overflow")]
    [DataRow("legacy-output-low")] [DataRow("legacy-output-unknown")]
    [DataRow("legacy-input-low")] [DataRow("legacy-input-unknown")]
    public async Task Output_budget_inconsistent_node_usage_rejects_before_client_creation(string fault)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var legacy = fault.StartsWith("legacy", StringComparison.Ordinal);
                var state = ReceiptState(run, legacy || fault == "sum-overflow" ? 3 : 2);
                if (legacy)
                {
                    state.Binding = state.Binding! with { LegacyPrefixCount = 2 };
                    state.NodeReceipts.RemoveRange(0, 2);
                }
                var index = legacy ? 0 : 1;
                switch (fault)
                {
                    case "output-low": case "legacy-output-low": state.OutputTokens = 0; break;
                    case "output-high": state.OutputTokens = 90; break;
                    case "output-unknown": case "legacy-output-unknown":
                        state.NodeReceipts[index] = state.NodeReceipts[index] with { OutputTokens = null };
                        state.OutputTokens = 0;
                        break;
                    case "input-low": case "legacy-input-low": state.InputTokens = 0; break;
                    case "input-unknown": case "legacy-input-unknown":
                        state.NodeReceipts[index] = state.NodeReceipts[index] with { InputTokens = null };
                        break;
                    case "sum-overflow":
                        state.NodeReceipts[1] = state.NodeReceipts[1] with { OutputTokens = long.MaxValue };
                        state.NodeReceipts[2] = state.NodeReceipts[2] with { OutputTokens = 1 };
                        break;
                }
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("unknown")] [DataRow("exhausted")] [DataRow("output-over")]
    [DataRow("input-over")] [DataRow("zero-output")] [DataRow("zero-input")]
    public async Task Output_budget_invalid_restore_rejects_before_client_creation(string fault)
    {
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            configureRecord: run => SetOutputBudget(run, fault == "zero-input" ? 0 : 100, fault == "zero-output" ? 0 : 100),
            checkpointFactory: run =>
            {
                var state = State(run.SessionId, 2);
                state.InputTokens = fault == "input-over" ? 101 : 7;
                state.OutputTokens = fault switch { "unknown" => null, "exhausted" => 100, "output-over" => 101, _ => 90 };
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Output_budget_restored_usage_limits_remaining_model_request()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"decision":"approved"}"""))
                { Usage = new() { InputTokenCount = 1, OutputTokenCount = 1 } });
        var runner = CallingModelRunner();
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, chatClient: client, runner: runner,
            checkpointFactory: run => { var state = State(run.SessionId, 2); state.OutputTokens = 90; return Checkpoint(run, state); });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Is<ChatOptions?>(o => o != null && o.MaxOutputTokens == 10), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Is<AiOperationUsage>(u => u != null && u.OutputTokens == 91), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Output_budget_completed_unknown_history_is_settled_without_dispatch()
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run => Checkpoint(run, State(run.SessionId, 4)));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Is<AiOperationUsage>(u => u != null && u.OutputTokens == null), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Output_budget_current_exhaustion_never_dispatches_second_model_or_retains_false_intent()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Title"))
                { Usage = new() { InputTokenCount = 1, OutputTokenCount = 100 } });
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, chatClient: client, runner: CallingModelRunner());
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        var checkpoints = fixture.Commands.ReceivedCalls().Where(call => call.GetArguments()[0] is Full.NET.Data.Abstractions.SqlStatement statement && statement == AiAgentRunSql.InsertCheckpoint).ToArray();
        var last = Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(checkpoints[^1].GetArguments()[1]);
        var state = AgentWorkflowState.FromJson(Assert.IsInstanceOfType<string>(last["PayloadProtected"]));
        Assert.IsNull(state!.PendingNode);
        Assert.AreEqual(2, state.NextNodeIndex);
        Assert.AreEqual(100L, state.OutputTokens);
    }

    private static IAgentModelRunner CallingModelRunner()
    {
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => CallAsync(call.ArgAt<IChatClient>(0), call.ArgAt<CancellationToken>(2)));
        return runner;

        static async Task<AgentModelResult> CallAsync(IChatClient client, CancellationToken cancellationToken)
        {
            var response = await client.GetResponseAsync([], cancellationToken: cancellationToken);
            return new(response.Text, JsonSerializer.SerializeToElement(true), response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
        }
    }

    private static void SetOutputBudget(AiAgentRunRecord run, long input, long output)
    {
        var snapshot = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot)!;
        run.BudgetJson = JsonSerializer.Serialize(snapshot with { InputTokenLimit = input, OutputTokenLimit = output }, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
    }
}
