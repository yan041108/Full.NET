using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiAgentRunCoordinator。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow("run")]
    [DataRow("version")]
    [DataRow("id")]
    [DataRow("sequence")]
    [DataRow("sequence-max")]
    [DataRow("format")]
    [DataRow("framework")]
    [DataRow("checksum")]
    [DataRow("null-checksum")]
    [DataRow("null-payload")]
    [DataRow("session")]
    [DataRow("malformed")]
    [DataRow("null-state")]
    [DataRow("null-outputs")]
    [DataRow("null-value")]
    [DataRow("cursor-low")]
    [DataRow("cursor-high")]
    [DataRow("input-negative")]
    [DataRow("output-negative")]
    [DataRow("missing-output")]
    [DataRow("empty-output")]
    [DataRow("blank-output")]
    [DataRow("validation")]
    [DataRow("title")]
    public async Task Incompatible_workflow_checkpoint_rejects_before_client_creation(string fault)
    {
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run => FaultedCheckpoint(run, fault));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        AssertStatusCommit(fixture.Commands, "failed");
        var append = fixture.Commands.ReceivedCalls().Single(c => c.GetArguments()[0] is Full.NET.Data.Abstractions.SqlStatement s && s == AiAgentRunSql.InsertEvent);
        var parameters = Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(append.GetArguments()[1]);
        StringAssert.Contains(Assert.IsInstanceOfType<string>(parameters["Payload"]), "ai.agent_run.checkpoint_incompatible");
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == null && u.OutputTokens == null), "failed", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unknown_definition_rejects_before_client_creation(bool obsoleteWorkflow)
    {
        var fixture = await CreateFixtureAsync(
            definitionKey: obsoleteWorkflow ? AgentWorkflowRegistry.ChatRenameWorkflowKey : "unregistered",
            configureRecord: run => run.DefinitionVersion = obsoleteWorkflow ? 1 : 999);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        AssertStatusCommit(fixture.Commands, "failed");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_during_checkpoint_read_never_creates_client_or_dispatches(bool incompatible)
    {
        using var source = new CancellationTokenSource();
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run => { source.Cancel(); return incompatible ? FaultedCheckpoint(run, "checksum") : Checkpoint(run, State(run.SessionId, 3)); });
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.ProcessPendingAsync(source.Token));
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public async Task Valid_checkpoint_runs_only_remaining_nodes_and_preserves_unknown_usage(int next)
    {
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new AgentModelResult(call.ArgAt<string>(1).Contains("session list", StringComparison.Ordinal) ? "Title" : """{"decision":"approved"}""", JsonSerializer.SerializeToElement(true), 2, 1));
        var fixture = await CreateFixtureAsync(
            definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            runner: runner,
            checkpointFactory: run =>
            {
                var state = State(run.SessionId, next);
                if (next == 2) state.OutputTokens = 1;
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        Assert.AreEqual(next < 2 ? 2 : next == 2 ? 1 : 0, runner.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAgentModelRunner.RunAsync)));
        await fixture.ToolExecutor.Received(next == 0 ? 1 : 0).ExecuteAsync(Arg.Is<ToolInvocation>(t => t != null && t.ToolName == "ai.chat.sessions.list"), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.Received(next < 4 ? 1 : 0).ExecuteAsync(Arg.Is<ToolInvocation>(t => t != null && t.ToolName == "ai.chat.sessions.rename"), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == (next < 2 ? 4 : next == 2 ? 9 : 7) && u.OutputTokens == (next < 3 ? 2 : null)), "succeeded", Arg.Any<CancellationToken>());
    }

    private static AgentWorkflowState State(Guid sessionId, int next)
    {
        var outputs = new Dictionary<string, string> { ["read_sessions"] = "[]", ["summarize"] = "Title", ["validate"] = """{"decision":"approved"}""", ["rename"] = "true" };
        var state = AgentWorkflowState.Create(sessionId);
        state.NextNodeIndex = next;
        state.ExecutionSteps = next;
        foreach (var entry in outputs.Take(next)) state.Outputs.Add(entry.Key, entry.Value);
        if (next >= 2) state.InputTokens = 7;
        return state;
    }

    private static AgentCheckpointRecord Checkpoint(AiAgentRunRecord run, AgentWorkflowState state, string? json = null)
    {
        var payload = json ?? state.ToJson();
        return new(Guid.CreateVersion7(), run.Id, 1, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, payload, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }

    private static AgentCheckpointRecord FaultedCheckpoint(AiAgentRunRecord run, string fault)
    {
        var state = State(run.SessionId, 3);
        switch (fault)
        {
            case "session": state.SessionId = Guid.CreateVersion7(); break;
            case "null-outputs": state.Outputs = null!; break;
            case "null-value": state.Outputs["read_sessions"] = null!; break;
            case "cursor-low": state.NextNodeIndex = -1; break;
            case "cursor-high": state.NextNodeIndex = 5; break;
            case "input-negative": state.InputTokens = -1; break;
            case "output-negative": state.OutputTokens = -1; break;
            case "missing-output": state.Outputs.Remove("read_sessions"); break;
            case "empty-output": state.Outputs["read_sessions"] = string.Empty; break;
            case "blank-output": state.Outputs["read_sessions"] = " \t\r\n"; break;
            case "validation": state.Outputs["validate"] = """{"decision":"rejected"}"""; break;
            case "title": state.Outputs["summarize"] = new('x', 257); break;
        }
        var checkpoint = Checkpoint(run, state, fault == "malformed" ? "{" : fault == "null-state" ? "null" : null);
        return fault switch
        {
            "run" => checkpoint with { RunId = Guid.CreateVersion7() },
            "version" => checkpoint with { DefinitionVersion = 1 },
            "id" => checkpoint with { CheckpointId = Guid.Empty },
            "sequence" => checkpoint with { Sequence = 0 },
            "sequence-max" => checkpoint with { Sequence = long.MaxValue },
            "format" => checkpoint with { FormatVersion = 999 },
            "framework" => checkpoint with { FrameworkVersion = "unknown" },
            "checksum" => checkpoint with { Checksum = new('0', 64) },
            "null-checksum" => checkpoint with { Checksum = null! },
            "null-payload" => checkpoint with { PayloadProtected = null! },
            _ => checkpoint,
        };
    }
}
