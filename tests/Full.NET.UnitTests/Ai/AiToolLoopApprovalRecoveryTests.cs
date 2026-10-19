using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiAgentRunCoordinator。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    public async Task Approval_cursor_rechecks_original_operation_and_keeps_confirmed_prefix()
    {
        var client = RecoveryLoopClient();
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client, configureRecord: run => run.LeaseEpoch = 2,
            checkpointFactory: ApprovalCursor);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.ToolExecutor.Received(1).ExecuteAsync(Arg.Is<ToolInvocation>(i =>
            i != null && i.OperationId == CursorOperationId(fixture.RunId, "second")
            && i.ToolVersion == 1 && i.Arguments.GetRawText() == "{}"), Arg.Any<CancellationToken>());
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    private static AgentCheckpointRecord ApprovalCursor(AiAgentRunRecord run)
    {
        var checkpoint = CursorCheckpoint(run, loop =>
        {
            loop["Phase"] = "blocked";
            var call = loop["Rounds"]![0]!["Messages"]![0]!["Contents"]![1]!;
            call["OperationId"] = CursorOperationId(run.Id, "second");
            call["ResultPayload"] = """{"statusKey":"denied","errorCode":"ai.tool.approval_required","value":null,"isUntrusted":true}""";
        });
        var envelope = JsonNode.Parse(checkpoint.PayloadProtected)!;
        envelope["Phase"] = "started";
        var json = envelope.ToJsonString();
        return checkpoint with { PayloadProtected = json, Checksum = SingleDigest(json) };
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Approval_cursor_rechecks_current_denial_without_replaying_model(bool unknown)
    {
        var client = RecoveryLoopClient(); var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(
            new ToolExecutionResult(unknown ? "failed" : "denied", null,
                unknown ? "ai.tool.reconciliation_required" : "ai.tool.approval_required"));
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client, toolExecutor: executor, checkpointFactory: ApprovalCursor);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, unknown ? "reconciliation_required" : "awaiting_approval");
        await executor.Received(1).ExecuteAsync(Arg.Is<ToolInvocation>(i => i != null && i.OperationId == CursorOperationId(fixture.RunId, "second")), Arg.Any<CancellationToken>());
        await client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("unknown")] [DataRow("exhausted")] [DataRow("model")]
    public async Task Approval_cursor_invalid_usage_or_model_binding_never_creates_client(string fault)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: run =>
            {
                var checkpoint = ApprovalCursor(run); var body = JsonNode.Parse(checkpoint.PayloadProtected)!;
                if (fault == "model") body["ModelConfigVersion"] = 2;
                else
                {
                    var loop = JsonNode.Parse(body["LoopState"]!.GetValue<string>())!;
                    loop["Rounds"]![0]!["InputTokens"] = fault == "unknown" ? null : JsonValue.Create(100);
                    body["LoopState"] = loop.ToJsonString();
                }
                var json = body.ToJsonString(); return checkpoint with { PayloadProtected = json, Checksum = SingleDigest(json) };
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<Full.NET.AI.Abstractions.Models.ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Approval_cursor_revoked_session_never_creates_client_or_tool()
    {
        var fixture = await CreateFixtureAsync(bindingValid: false, definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: ApprovalCursor);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "authorization_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<Full.NET.AI.Abstractions.Models.ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Approval_cursor_lost_commit_never_dispatches_original_tool()
    {
        var fixture = await CreateFixtureAsync(commitSucceeds: false, definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: ApprovalCursor);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }
}
