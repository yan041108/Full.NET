using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AgentWorkflowRunnerTests
{
    [TestMethod]
    [DataRow(null)] [DataRow(-1)] [DataRow(0)] [DataRow(2)] [DataRow(64)] [DataRow(65)] [DataRow(int.MaxValue)]
    public async Task Unknown_invalid_or_exhausted_steps_cannot_dispatch_remaining_node(int? steps)
    {
        var state = StepState(steps);
        var executor = CreateExecutor("[]");
        var runner = CreateModelRunner("Title", """{"decision":"approved"}""");
        var result = await RunSteps(state, runner, executor);
        Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status);
        Assert.AreEqual("ai.agent_run.tool_budget_exceeded", result.ErrorCode);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Last_step_is_persisted_before_dispatch_and_is_not_refunded_for_approval()
    {
        var state = StepState(63);
        var executor = CreateExecutor("[]", "denied");
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("denied", null, "ai.tool.approval_required"));
        string? durable = null;
        var request = StepRequest(state, CreateModelRunner("Title", "approved"), executor) with
        {
            PersistNodeAsync = (_, current, _) =>
            {
                durable = current.ToJson();
                Assert.AreEqual(64, JsonNode.Parse(durable)!["ExecutionSteps"]!.GetValue<int>());
                return Task.FromResult(true);
            },
        };
        var first = await new AgentWorkflowRunner().RunAsync(request);
        Assert.AreEqual(AgentWorkflowRunStatus.AwaitingApproval, first.Status);
        Assert.IsNotNull(durable);
        Assert.AreEqual(64, JsonNode.Parse(first.State.ToJson())!["ExecutionSteps"]!.GetValue<int>());
        var restored = AgentWorkflowState.FromJson(first.State.ToJson())!;
        var second = await RunSteps(restored, request.ModelRunner, executor);
        Assert.AreEqual("ai.agent_run.tool_budget_exceeded", second.ErrorCode);
        await executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(64)] [DataRow(null)]
    public async Task Completed_workflow_with_exhausted_or_unknown_steps_finalizes_without_new_dispatch(int? steps)
    {
        var state = StepState(steps);
        state.NextNodeIndex = 4; state.Outputs["rename"] = "true";
        var executor = CreateExecutor("[]");
        var result = await RunSteps(state, CreateModelRunner("Title", "approved"), executor);
        Assert.AreEqual(AgentWorkflowRunStatus.Completed, result.Status);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Duplicate_step_counter_is_not_last_value_wins()
    {
        var json = StepState(64).ToJson();
        json = json[..^1] + ",\"ExecutionSteps\":3}";
        Assert.ThrowsExactly<JsonException>(() => AgentWorkflowState.FromJson(json));
    }

    [TestMethod]
    public async Task Model_node_uses_last_step_and_stops_before_next_model()
    {
        var state = StepState(63); state.NextNodeIndex = 1;
        state.Outputs.Remove("summarize"); state.Outputs.Remove("validate");
        var runner = CreateModelRunner("Title", "approved");
        var executor = CreateExecutor("[]");
        var result = await RunSteps(state, runner, executor);
        Assert.AreEqual("ai.agent_run.tool_budget_exceeded", result.ErrorCode);
        Assert.AreEqual(2, result.State.NextNodeIndex);
        Assert.AreEqual(64, result.State.ExecutionSteps);
        await runner.Received(1).RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_before_dispatch_preserves_conservative_counter(bool afterIntent)
    {
        var state = StepState(63); using var source = new CancellationTokenSource();
        var executor = CreateExecutor("[]");
        if (!afterIntent) source.Cancel();
        var request = StepRequest(state, CreateModelRunner("Title", "approved"), executor) with
        {
            PersistNodeAsync = (_, _, _) => { source.Cancel(); return Task.FromResult(true); },
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AgentWorkflowRunner().RunAsync(request, source.Token));
        Assert.AreEqual(afterIntent ? 64 : 63, state.ExecutionSteps);
        Assert.AreEqual(afterIntent, state.PendingNode is not null);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Failed_counter_intent_commit_never_dispatches()
    {
        var state = StepState(63); var executor = CreateExecutor("[]");
        var request = StepRequest(state, CreateModelRunner("Title", "approved"), executor) with
        { PersistNodeAsync = (_, _, _) => Task.FromResult(false) };
        var result = await new AgentWorkflowRunner().RunAsync(request);
        Assert.AreEqual(AgentWorkflowRunStatus.ReconciliationRequired, result.Status);
        Assert.AreEqual(64, state.ExecutionSteps);
        Assert.IsNotNull(state.PendingNode);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    private static AgentWorkflowState StepState(int? steps)
    {
        var state = AgentWorkflowState.Create(Guid.CreateVersion7());
        state.NextNodeIndex = 3;
        state.Outputs["read_sessions"] = "[]"; state.Outputs["summarize"] = "Title";
        state.Outputs["validate"] = """{"decision":"approved"}""";
        var json = JsonNode.Parse(state.ToJson())!;
        json["ExecutionSteps"] = steps;
        return AgentWorkflowState.FromJson(json.ToJsonString())!;
    }

    private static AgentWorkflowRunRequest StepRequest(AgentWorkflowState state, IAgentModelRunner runner, IAgentToolExecutor executor) =>
        new(Guid.CreateVersion7(), AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion,
            state, Substitute.For<IChatClient>(), runner, executor, CreateRegistry(), _ => Guid.CreateVersion7());

    private static Task<AgentWorkflowRunResult> RunSteps(AgentWorkflowState state, IAgentModelRunner runner, IAgentToolExecutor executor) =>
        new AgentWorkflowRunner().RunAsync(StepRequest(state, runner, executor));
}
