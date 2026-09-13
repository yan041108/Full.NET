using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>显式工作流在节点边界推进、校验门禁与写工具审批时保持受控语义。</summary>
[TestClass]
public sealed class AgentWorkflowRunnerTests
{
    [TestMethod]
    public async Task Happy_path_completes_all_nodes_async()
    {
        var sessionId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var executor = CreateExecutor(
            readOutput: """{"sessions":[{"id":"1","title":"old"}]}""",
            renameStatus: "succeeded");
        var runner = CreateModelRunner("Proposed Title", "VALID");
        var registry = CreateRegistry();
        var state = AgentWorkflowState.Create(sessionId);
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            runId,
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            1,
            state,
            Substitute.For<IChatClient>(),
            runner,
            executor,
            registry,
            key => Guid.CreateVersion7()), CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(AgentWorkflowRunStatus.Completed, result.Status);
        Assert.AreEqual("Proposed Title", result.FinalText);
        Assert.AreEqual(4, result.State.NextNodeIndex);
        await executor.Received(1).ExecuteAsync(
            Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.rename"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Validation_failure_stops_before_rename_async()
    {
        var sessionId = Guid.CreateVersion7();
        var executor = CreateExecutor(readOutput: """{"sessions":[]}""");
        var runner = CreateModelRunner("Bad Title", "INVALID unsafe");
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            Guid.CreateVersion7(),
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            1,
            AgentWorkflowState.Create(sessionId),
            Substitute.For<IChatClient>(),
            runner,
            executor,
            CreateRegistry(),
            _ => Guid.CreateVersion7()), CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status);
        Assert.AreEqual("ai.agent_run.workflow_validation_failed", result.ErrorCode);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.rename"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Rename_approval_required_persists_node_index_async()
    {
        var sessionId = Guid.CreateVersion7();
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(
                Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.list"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement("[]"), null));
        executor.ExecuteAsync(
                Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.rename"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("denied", null, "ai.tool.approval_required"));
        var runner = CreateModelRunner("Rename Me", "VALID");
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            Guid.CreateVersion7(),
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            1,
            AgentWorkflowState.Create(sessionId),
            Substitute.For<IChatClient>(),
            runner,
            executor,
            CreateRegistry(),
            _ => Guid.CreateVersion7()), CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(AgentWorkflowRunStatus.AwaitingApproval, result.Status);
        Assert.AreEqual(3, result.State.NextNodeIndex);
        Assert.AreEqual("Rename Me", result.State.Outputs["summarize"]);
    }

    [TestMethod]
    public async Task Resume_from_checkpoint_retries_rename_only_async()
    {
        var sessionId = Guid.CreateVersion7();
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(new { sessionId, title = "Done" }), null));
        var runner = Substitute.For<IAgentModelRunner>();
        var state = new AgentWorkflowState
        {
            SessionId = sessionId,
            NextNodeIndex = 3,
            Outputs =
            {
                ["read_sessions"] = "[]",
                ["summarize"] = "Done",
                ["validate"] = "VALID",
            },
        };
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            Guid.CreateVersion7(),
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            1,
            state,
            Substitute.For<IChatClient>(),
            runner,
            executor,
            CreateRegistry(),
            _ => Guid.CreateVersion7()), CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(AgentWorkflowRunStatus.Completed, result.Status);
        await runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await executor.Received(1).ExecuteAsync(
            Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.rename"),
            Arg.Any<CancellationToken>());
    }

    private static IAgentModelRunner CreateModelRunner(string summarizeText, string validateText)
    {
        var runner = Substitute.For<IAgentModelRunner>();
        var session = JsonSerializer.SerializeToElement(new { messages = Array.Empty<string>() });
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                call => new AgentModelResult(
                    call.ArgAt<string>(1).Contains("session list", StringComparison.OrdinalIgnoreCase) ? summarizeText : validateText,
                    session,
                    3,
                    2));
        return runner;
    }

    private static IAgentToolExecutor CreateExecutor(string readOutput, string renameStatus = "succeeded")
    {
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(
                Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.list"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult("succeeded", JsonDocument.Parse(readOutput).RootElement.Clone(), null));
        executor.ExecuteAsync(
                Arg.Is<ToolInvocation>(invocation => invocation != null && invocation.ToolName == "ai.chat.sessions.rename"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolExecutionResult(renameStatus, JsonSerializer.SerializeToElement(new { ok = true }), null));
        return executor;
    }

    private static AgentToolRegistry CreateRegistry() => new([
        new AgentToolDefinition("ai.chat.sessions.list", 1, "ai.chat.read", "read", true, null),
        new AgentToolDefinition("ai.chat.sessions.rename", 1, "ai.chat.write", "write", true, null),
    ]);
}
