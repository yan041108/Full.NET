using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>脚本化模型响应验证统一执行器、稳定 OperationId 与未注册工具拒绝。</summary>
[TestClass]
public sealed class AiAgentToolLoopTests
{
    private static readonly Guid RunId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Tool_call_routes_through_executor_and_returns_final_text_async()
    {
        var client = Substitute.For<IChatClient>();
        var callId = "call-1";
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var registry = new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]);
        var resolvedCallIds = new List<string>();
        var loop = new AgentToolLoop();
        var result = await loop.RunAsync(new(
            RunId,
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            "hello",
            client,
            executor,
            registry,
            callId => { resolvedCallIds.Add(callId); return Guid.CreateVersion7(); }), CancellationToken.None);
        Assert.AreEqual("done", result.Text);
        Assert.AreEqual(1, result.ToolSteps);
        var invocation = executor.ReceivedCalls().Single().GetArguments()[0] as ToolInvocation;
        Assert.IsNotNull(invocation);
        Assert.AreEqual(RunId, invocation.RunId);
        Assert.AreEqual("ai.tools.ping", invocation.ToolName);
        CollectionAssert.AreEqual(new[] { callId }, resolvedCallIds);
        await client.Received(2).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Operation_id_resolver_receives_model_call_id_async()
    {
        var client = Substitute.For<IChatClient>();
        const string callId = "stable-call";
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var registry = new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]);
        var resolved = new List<string>();
        var loop = new AgentToolLoop();
        await loop.RunAsync(new(
            RunId,
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            "x",
            client,
            executor,
            registry,
            callId => { resolved.Add(callId); return Guid.CreateVersion7(); }), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { callId }, resolved);
    }

    [TestMethod]
    public async Task Unregistered_tool_is_not_dispatched_to_executor_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-x", "unknown.tool", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback")));
        var executor = Substitute.For<IAgentToolExecutor>();
        var registry = new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]);
        var loop = new AgentToolLoop();
        await loop.RunAsync(new(RunId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "x", client, executor, registry, _ => Guid.CreateVersion7()), CancellationToken.None);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reconciliation_required_is_surfaced_to_caller_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("failed", null, "ai.tool.reconciliation_required"));
        var loop = new AgentToolLoop();
        var result = await loop.RunAsync(new(
            Guid.CreateVersion7(),
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            "prompt",
            client,
            executor,
            new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]),
            _ => Guid.CreateVersion7()));
        Assert.IsTrue(result.ReconciliationRequired);
    }

    [TestMethod]
    public async Task Parallel_tool_calls_in_one_response_count_toward_tool_steps_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-a", "ai.tools.ping", new Dictionary<string, object?>()),
                    new FunctionCallContent("call-b", "ai.models.list", new Dictionary<string, object?>()),
                ])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "both")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var registry = new AgentToolRegistry([
            new("ai.tools.ping", 1, "permission", "none", true, null),
            new("ai.models.list", 1, "permission", "none", true, null),
        ]);
        var loop = new AgentToolLoop();
        var result = await loop.RunAsync(new(
            RunId,
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            "parallel",
            client,
            executor,
            registry,
            _ => Guid.CreateVersion7()), CancellationToken.None);
        Assert.AreEqual("both", result.Text);
        Assert.AreEqual(2, result.ToolSteps);
        await executor.Received(2).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Approval_required_result_sets_flag_without_throwing_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])),
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("denied", null, "ai.tool.approval_required"));
        var registry = new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "write", true, null)]);
        var loop = new AgentToolLoop();
        var result = await loop.RunAsync(new(
            RunId,
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            "approval",
            client,
            executor,
            registry,
            _ => Guid.CreateVersion7()), CancellationToken.None);
        Assert.AreEqual("done", result.Text);
        Assert.IsTrue(result.ApprovalRequired);
    }

    [TestMethod]
    public async Task Iteration_budget_throws_when_model_keeps_calling_tools_async()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("loop", "ai.tools.ping", new Dictionary<string, object?>())])));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var registry = new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]);
        var loop = new AgentToolLoop();
        await Assert.ThrowsAsync<AgentToolLoopBudgetExceededException>(() => loop.RunAsync(new(
            RunId,
            AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            1,
            "loop",
            client,
            executor,
            registry,
            _ => Guid.CreateVersion7()), CancellationToken.None));
    }
}
