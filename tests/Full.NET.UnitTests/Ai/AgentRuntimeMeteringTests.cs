using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>实际运行两个消费者，未知计量不被后续已知值覆盖，取消后不再派发或交付完成。</summary>
[TestClass]
public sealed class AgentRuntimeMeteringTests
{
    [TestMethod]
    [DataRow(false, -1L, 3L)]
    [DataRow(false, 3L, -1L)]
    [DataRow(false, -1L, -1L)]
    [DataRow(false, 3L, 4L)]
    [DataRow(false, 0L, 0L)]
    [DataRow(true, -1L, 3L)]
    [DataRow(true, 3L, -1L)]
    [DataRow(true, 3L, 4L)]
    public async Task Workflow_unknown_usage_remains_unknown_in_fresh_and_resumed_runs_async(bool resume, long first, long second)
    {
        var model = Substitute.For<IAgentModelRunner>();
        var state = State(resume ? 2 : 0); state.InputTokens = resume ? Usage(first) : null; state.OutputTokens = state.InputTokens;
        var responses = resume ? new[] { Model("{\"decision\":\"approved\"}", Usage(second), Usage(second)) }
            : [Model("Title", Usage(first), Usage(first)), Model("{\"decision\":\"approved\"}", Usage(second), Usage(second))];
        var queue = new Queue<AgentModelResult>(responses);
        model.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => queue.Dequeue());
        var result = await Workflow(state, model, Executor()).RunAsync();
        Assert.AreEqual(AgentWorkflowRunStatus.Completed, result.Status);
        long? expected = first < 0 || second < 0 ? null : first + second;
        Assert.AreEqual(expected, result.State.InputTokens); Assert.AreEqual(expected, result.State.OutputTokens);
    }

    [TestMethod]
    [DataRow(-1L, 3L)]
    [DataRow(3L, -1L)]
    [DataRow(-1L, -1L)]
    [DataRow(3L, 4L)]
    [DataRow(0L, 0L)]
    public async Task Tool_loop_unknown_usage_remains_unknown_across_responses_async(long first, long second)
    {
        var client = Client(CallResponse("one", Usage(first), Usage(first)), FinalResponse(Usage(second), Usage(second)));
        var result = await Loop(client, Executor()).RunAsync();
        long? expected = first < 0 || second < 0 ? null : first + second;
        Assert.AreEqual(expected, result.InputTokens); Assert.AreEqual(expected, result.OutputTokens);
    }

    [TestMethod]
    public async Task Input_and_output_unknown_totals_are_independent_async()
    {
        var client = Client(CallResponse("one", null, 2), FinalResponse(3, 4));
        var result = await Loop(client, Executor()).RunAsync();
        Assert.IsNull(result.InputTokens); Assert.AreEqual(6L, result.OutputTokens);
        var model = Substitute.For<IAgentModelRunner>();
        var queue = new Queue<AgentModelResult>([Model("Title", 2, null), Model("{\"decision\":\"approved\"}", 4, 3)]);
        model.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => queue.Dequeue());
        var workflow = await Workflow(State(0), model, Executor()).RunAsync();
        Assert.AreEqual(6L, workflow.State.InputTokens); Assert.IsNull(workflow.State.OutputTokens);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Overflow_usage_fails_without_dispatching_later_tools_async(bool workflow, bool output)
    {
        var executor = Executor();
        if (workflow)
        {
            var state = State(2); state.InputTokens = output ? 2 : long.MaxValue; state.OutputTokens = output ? long.MaxValue : 2;
            var model = Substitute.For<IAgentModelRunner>();
            model.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Model("{\"decision\":\"approved\"}", 1, 1));
            await Assert.ThrowsExactlyAsync<OverflowException>(() => Workflow(state, model, executor).RunAsync());
            Assert.AreEqual(output ? 2 : long.MaxValue, state.InputTokens); Assert.AreEqual(output ? long.MaxValue : 2, state.OutputTokens);
            Assert.AreEqual(2, state.NextNodeIndex); Assert.IsFalse(state.Outputs.ContainsKey("validate"));
        }
        else
        {
            var client = Client(CallResponse("one", output ? 2 : long.MaxValue, output ? long.MaxValue : 2), CallResponse("two", 1, 1));
            await Assert.ThrowsExactlyAsync<OverflowException>(() => Loop(client, executor).RunAsync());
            await executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
            return;
        }
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Negative_reported_usage_is_rejected_before_tools_async(bool workflow)
    {
        var executor = Executor();
        if (workflow)
        {
            var model = Substitute.For<IAgentModelRunner>();
            model.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Model("{\"decision\":\"approved\"}", -2, 1));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Workflow(State(2), model, executor).RunAsync());
        }
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Loop(Client(CallResponse("one", -2, 1)), executor).RunAsync());
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_during_operation_id_resolution_stops_actual_dispatch_async(bool workflow)
    {
        using var stop = new CancellationTokenSource(); var executor = Executor();
        Guid Resolve(string key) { stop.Cancel(); return Guid.CreateVersion7(); }
        if (workflow)
        {
            var call = Workflow(State(3), Substitute.For<IAgentModelRunner>(), executor, stop.Token);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new AgentWorkflowRunner().RunAsync(call.Request with { ResolveOperationId = Resolve }, stop.Token));
        }
        else
        {
            var call = Loop(Client(CallResponse("one", 2, 1)), executor, stop.Token);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new AgentToolLoop().RunAsync(call.Request with { ResolveOperationId = Resolve }, stop.Token));
        }
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Negative_checkpoint_usage_is_rejected_before_any_dispatch_async()
    {
        var state = State(2); state.InputTokens = -1; var model = Substitute.For<IAgentModelRunner>(); var executor = Executor();
        model.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Model("{\"decision\":\"approved\"}", 2, 1));
        var result = await Workflow(state, model, executor).RunAsync();
        Assert.AreEqual("ai.agent_run.checkpoint_incompatible", result.ErrorCode);
        await model.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancelled_final_model_response_cannot_complete_tool_loop_async()
    {
        using var stop = new CancellationTokenSource(); var client = Substitute.For<IChatClient>(); var executor = Executor();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { stop.Cancel(); return FinalResponse(2, 1); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Loop(client, executor, stop.Token).RunAsync());
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_between_tools_in_same_response_stops_next_dispatch_async()
    {
        using var stop = new CancellationTokenSource(); var executor = Executor();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(_ => { stop.Cancel(); return new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null); });
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("one", "ai.tools.ping", null), new FunctionCallContent("two", "ai.tools.ping", null)]));
        var client = Client(response, FinalResponse(2, 1));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Loop(client, executor, stop.Token).RunAsync());
        await executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancelled_workflow_wait_cannot_advance_or_report_completion_async(bool tool)
    {
        using var stop = new CancellationTokenSource(); var model = Substitute.For<IAgentModelRunner>(); var executor = Executor(); var state = State(tool ? 3 : 2);
        if (tool) executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(_ => { stop.Cancel(); return new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null); });
        else model.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { stop.Cancel(); return Model("{\"decision\":\"approved\"}", 2, 1); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Workflow(state, model, executor, stop.Token).RunAsync());
        Assert.AreEqual(tool ? 3 : 2, state.NextNodeIndex);
        if (!tool) await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    private static long? Usage(long value) => value < 0 ? null : value;
    private static AgentModelResult Model(string text, long? input, long? output) => new(text, JsonSerializer.SerializeToElement(new { messages = Array.Empty<string>() }), input, output);
    private static AgentWorkflowState State(int next)
    {
        var state = AgentWorkflowState.Create(Guid.CreateVersion7()); state.NextNodeIndex = next; state.ExecutionSteps = next;
        if (next >= 1) state.Outputs["read_sessions"] = "[]";
        if (next >= 2) state.Outputs["summarize"] = "Title";
        if (next >= 3) state.Outputs["validate"] = "{\"decision\":\"approved\"}";
        return state;
    }
    private static IAgentToolExecutor Executor()
    {
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        return executor;
    }
    private static IChatClient Client(params ChatResponse[] responses)
    {
        var client = Substitute.For<IChatClient>(); var queue = new Queue<ChatResponse>(responses);
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(_ => queue.Dequeue()); return client;
    }
    private static ChatResponse CallResponse(string id, long? input, long? output) => new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(id, "ai.tools.ping", null)])) { Usage = new() { InputTokenCount = input, OutputTokenCount = output } };
    private static ChatResponse FinalResponse(long? input, long? output) => new(new ChatMessage(ChatRole.Assistant, "done")) { Usage = new() { InputTokenCount = input, OutputTokenCount = output } };
    private static WorkflowCall Workflow(AgentWorkflowState state, IAgentModelRunner model, IAgentToolExecutor executor, CancellationToken token = default) => new(new(Guid.CreateVersion7(), AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion, state,
        Substitute.For<IChatClient>(), model, executor, new AgentToolRegistry([new("ai.chat.sessions.list", 1, "ai.chat.read", "read", true, null), new("ai.chat.sessions.rename", 1, "ai.chat.write", "write", true, null)]), _ => Guid.CreateVersion7()), token);
    private static LoopCall Loop(IChatClient client, IAgentToolExecutor executor, CancellationToken token = default) => new(new(Guid.CreateVersion7(), AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "hello", client, executor, new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]), _ => Guid.CreateVersion7()), token);
    private sealed record WorkflowCall(AgentWorkflowRunRequest Request, CancellationToken Token) { internal Task<AgentWorkflowRunResult> RunAsync() => new AgentWorkflowRunner().RunAsync(Request, Token); }
    private sealed record LoopCall(AgentToolLoopRequest Request, CancellationToken Token) { internal Task<AgentToolLoopResult> RunAsync() => new AgentToolLoop().RunAsync(Request, Token); }
}
