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
    [DataRow("NOT VALID")]
    [DataRow("VALID but unsafe")]
    [DataRow("VALID")]
    [DataRow("")]
    [DataRow("{\"decision\":\"rejected\"}")]
    [DataRow("{\"decision\":\"approved\",\"decision\":\"rejected\"}")]
    [DataRow("{\"decision\":\"rejected\",\"decision\":\"approved\"}")]
    [DataRow("{\"decision\":\"approved\",\"extra\":true}")]
    [DataRow("{\"decision\":\"APPROVED\"}")]
    [DataRow("{\"decision\":true}")]
    [DataRow("{}")]
    [DataRow("null")]
    [DataRow("{\"decision\":\"approved\"} trailing")]
    public async Task Untrusted_validation_never_dispatches_write_async(string validation)
    {
        // 同时验证正常执行和恢复路径，防止检查点绕过校验节点。
        foreach (var resume in new[] { false, true })
        {
            var state = AgentWorkflowState.Create(Guid.CreateVersion7());
            if (resume)
            {
                state.NextNodeIndex = 3;
                state.Outputs["summarize"] = "Title";
                state.Outputs["validate"] = validation;
            }
            var executor = CreateExecutor("[]");
            var result = await new AgentWorkflowRunner().RunAsync(new(
                Guid.CreateVersion7(), AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion,
                state, Substitute.For<IChatClient>(), CreateModelRunner("Title", validation),
                executor, CreateRegistry(), _ => Guid.CreateVersion7()));
            Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status);
            await executor.DidNotReceive().ExecuteAsync(
                Arg.Is<ToolInvocation>(x => x != null && x.ToolName == "ai.chat.sessions.rename"), Arg.Any<CancellationToken>());
        }
    }

    [TestMethod]
    public async Task Happy_path_completes_all_nodes_async()
    {
        var sessionId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var executor = CreateExecutor(
            readOutput: """{"sessions":[{"id":"1","title":"old"}]}""",
            renameStatus: "succeeded");
        var runner = CreateModelRunner("Proposed Title", """{"decision":"approved"}""");
        var registry = CreateRegistry();
        var state = AgentWorkflowState.Create(sessionId);
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            runId,
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion,
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
            AgentWorkflowRegistry.ChatRenameWorkflowVersion,
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
        var runner = CreateModelRunner("Rename Me", """{"decision":"approved"}""");
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            Guid.CreateVersion7(),
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion,
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
                ["validate"] = """{"decision":"approved"}""",
            },
        };
        var workflow = new AgentWorkflowRunner();
        var result = await workflow.RunAsync(new(
            Guid.CreateVersion7(),
            AgentWorkflowRegistry.ChatRenameWorkflowKey,
            AgentWorkflowRegistry.ChatRenameWorkflowVersion,
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

    [TestMethod]
    public async Task Invalid_title_or_missing_validation_cannot_resume_write_async()
    {
        foreach (var title in new[] { "", " ", new string('x', 257), "Valid title" })
        {
            var state = AgentWorkflowState.Create(Guid.CreateVersion7());
            state.NextNodeIndex = 3;
            state.Outputs["summarize"] = title;
            // 最后一个场景缺失校验结果，其余场景即使模型批准也不能通过业务规则。
            if (title != "Valid title") state.Outputs["validate"] = """{"decision":"approved"}""";
            var executor = CreateExecutor("[]");
            var result = await new AgentWorkflowRunner().RunAsync(new(
                Guid.CreateVersion7(), AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion,
                state, Substitute.For<IChatClient>(), CreateModelRunner(title, """{"decision":"approved"}"""),
                executor, CreateRegistry(), _ => Guid.CreateVersion7()));
            Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status);
            await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        }
    }

    [TestMethod]
    public async Task Legacy_definition_is_rejected_without_model_or_tool_execution_async()
    {
        var runner = CreateModelRunner("Title", "VALID");
        var executor = CreateExecutor("[]");
        var state = AgentWorkflowState.Create(Guid.CreateVersion7());
        state.NextNodeIndex = 3;
        state.Outputs["summarize"] = "Title";
        state.Outputs["validate"] = "VALID";
        var result = await new AgentWorkflowRunner().RunAsync(new(
            Guid.CreateVersion7(), AgentWorkflowRegistry.ChatRenameWorkflowKey, 1,
            state, Substitute.For<IChatClient>(), runner, executor, CreateRegistry(), _ => Guid.CreateVersion7()));
        Assert.AreEqual("ai.agent_run.definition_incompatible", result.ErrorCode);
        Assert.IsFalse(AgentCheckpointCompatibility.TryValidateWorkflow(
            AgentWorkflowRegistry.ChatRenameWorkflowKey, 1, 1, AgentFrameworkRuntime.FrameworkVersion, out _));
        await runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
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
