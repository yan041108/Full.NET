using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Workflows;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiAgentRunCoordinator。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(null)] [DataRow(-1)] [DataRow(0)] [DataRow(2)] [DataRow(64)] [DataRow(65)] [DataRow(int.MaxValue)]
    public async Task Restored_step_budget_rejects_before_client_factory(int? steps)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = State(run.SessionId, 3);
                var json = JsonNode.Parse(state.ToJson())!; json["ExecutionSteps"] = steps;
                return Checkpoint(run, state, json.ToJsonString());
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_legacy_step_counter_does_not_become_zero()
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = State(run.SessionId, 3);
                var json = JsonNode.Parse(state.ToJson())!.AsObject(); json.Remove("ExecutionSteps");
                return Checkpoint(run, state, json.ToJsonString());
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(null)] [DataRow(0)]
    public async Task Unknown_pending_dispatch_keeps_reconciliation_priority_over_step_budget(int? steps)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = ReceiptState(run, 1, pending: true);
                var json = JsonNode.Parse(state.ToJson())!; json["ExecutionSteps"] = steps;
                return Checkpoint(run, state, json.ToJsonString());
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(null)] [DataRow(64)]
    public async Task Completed_checkpoint_finalizes_with_unknown_or_exhausted_steps(int? steps)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = State(run.SessionId, 4);
                var json = JsonNode.Parse(state.ToJson())!; json["ExecutionSteps"] = steps;
                return Checkpoint(run, state, json.ToJsonString());
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        Assert.AreEqual(0, fixture.Runner.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "RunAsync"));
        Assert.AreEqual(0, fixture.ToolExecutor.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "ExecuteAsync"));
    }
}
