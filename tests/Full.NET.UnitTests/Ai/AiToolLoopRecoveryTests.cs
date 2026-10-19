using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiAgentRunCoordinator。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void Durable_tool_loop_expired_scan_includes_definition_with_existing_receipt(bool mysql)
    {
        var sql = mysql ? AiAgentRunSql.ScanQueuedMySql : AiAgentRunSql.ScanQueuedSqlServer;
        Assert.Contains("@ToolLoopKey", sql.Text);
        Assert.Contains("run.node_started", sql.Text);
        Assert.Contains("fn_ai_agent_checkpoint", sql.Text);
    }

    [TestMethod]
    public async Task Durable_tool_loop_commits_intent_before_provider_and_receipt_before_settlement()
    {
        CoordinatorFixture? fixture = null;
        var client = RecoveryLoopClient();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.AreEqual("started", LatestLoopPhase(fixture!.Commands));
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "reply"))
                    { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } };
            });
        fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey, chatClient: client);
        fixture.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Assert.AreEqual("completed", LatestLoopPhase(fixture.Commands)); return Task.CompletedTask; });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
    }

    [TestMethod]
    public async Task Durable_tool_loop_lost_intent_fence_never_dispatches()
    {
        var client = RecoveryLoopClient();
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client, commitSucceeds: false);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        await client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Durable_tool_loop_unknown_or_missing_prior_intent_never_replays(bool checkpoint)
    {
        var client = RecoveryLoopClient();
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client, configureRecord: run => run.LeaseEpoch = 2,
            checkpointFactory: checkpoint ? run => SingleCheckpoint(run) : null);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(2L, 1L)] [DataRow(null, null)] [DataRow(100L, 100L)]
    public async Task Durable_tool_loop_completed_receipt_recovers_without_client_or_tool(long? input, long? output)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: run => CompletedSingleCheckpoint(run, input, output));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId,
            Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == input && u.OutputTokens == output), "succeeded", Arg.Any<CancellationToken>());
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.InsertStep));
    }

    [TestMethod]
    [DataRow("session")] [DataRow("model")] [DataRow("input")] [DataRow("checksum")]
    [DataRow("definition")] [DataRow("budget")] [DataRow("usage")] [DataRow("duplicate")]
    [DataRow("legacy")] [DataRow("sequence")]
    public async Task Durable_tool_loop_invalid_receipt_preserves_budget_without_replay(string fault)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: run =>
            {
                var checkpoint = CompletedSingleCheckpoint(run);
                var body = JsonNode.Parse(checkpoint.PayloadProtected)!;
                switch (fault)
                {
                    case "session": body["SessionId"] = Guid.CreateVersion7(); break;
                    case "model": body["ModelConfigVersion"] = 2; break;
                    case "input": body["InputDigest"] = SingleDigest("different"); break;
                    case "definition": body["DefinitionKey"] = AgentDefinitionRegistry.SingleTextKey; break;
                    case "budget": body["BudgetDigest"] = new string('0', 64); break;
                    case "usage": body["Result"]!["InputTokens"] = 101; break;
                }
                var json = body.ToJsonString();
                if (fault == "duplicate") json = json[..^1] + ",\"Phase\":\"started\"}";
                if (fault == "legacy") json = "{\"messages\":[]}";
                return checkpoint with { PayloadProtected = json, Checksum = fault == "checksum" ? "invalid" : SingleDigest(json),
                    Sequence = fault == "sequence" ? 0 : checkpoint.Sequence };
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Durable_tool_loop_revoked_session_preserves_prior_budget(bool completed)
    {
        var fixture = await CreateFixtureAsync(bindingValid: false, definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: run => completed ? CompletedSingleCheckpoint(run) : SingleCheckpoint(run));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "authorization_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Durable_tool_loop_provider_exception_retains_intent_and_budget()
    {
        var client = RecoveryLoopClient();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("uncertain provider result"));
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey, chatClient: client);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        Assert.AreEqual("started", LatestLoopPhase(fixture.Commands));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Durable_tool_loop_later_local_budget_refusal_cannot_prove_whole_loop_not_dispatched()
    {
        var client = RecoveryLoopClient();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())]))
                { Usage = new() { InputTokenCount = 100, OutputTokenCount = 1 } });
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey, chatClient: client);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        Assert.AreEqual("started", LatestLoopPhase(fixture.Commands));
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IChatClient RecoveryLoopClient()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "reply"))
                { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } });
        return client;
    }

    [TestMethod]
    [DataRow(1L)] [DataRow(2L)]
    public async Task Durable_tool_loop_unreadable_checkpoint_never_implies_no_dispatch(long epoch)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            configureRecord: run => run.LeaseEpoch = epoch,
            checkpointFactory: _ => throw new InvalidOperationException("checkpoint unavailable"));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Durable_tool_loop_failed_finalization_keeps_completed_receipt(bool settlementFails)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: RecoveryLoopClient(), configureCommands: commands =>
            {
                if (!settlementFails)
                    commands.ExecuteAsync(AiAgentRunSql.UpdateRunStatus,
                        Arg.Is<object?>(value => value is IReadOnlyDictionary<string, object?> &&
                            ((IReadOnlyDictionary<string, object?>)value)["StatusKey"] as string == "completed"),
                        Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new InvalidOperationException("completion unavailable"));
            });
        if (settlementFails)
            fixture.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new InvalidOperationException("settlement unavailable"));
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual("completed", LatestLoopPhase(fixture.Commands));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Durable_tool_loop_step_exhaustion_preserves_uncertain_run_budget()
    {
        var client = RecoveryLoopClient();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())]))
                { Usage = new() { InputTokenCount = 0, OutputTokenCount = 0 } });
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey, chatClient: client);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        Assert.AreEqual("started", LatestLoopPhase(fixture.Commands));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static string? LatestLoopPhase(ICommandExecutor commands)
    {
        var call = commands.ReceivedCalls().LastOrDefault(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.InsertCheckpoint);
        if (call is null) return null;
        var values = (IReadOnlyDictionary<string, object?>)call.GetArguments()[1]!;
        using var document = JsonDocument.Parse((string)values["PayloadProtected"]!);
        return document.RootElement.TryGetProperty("Phase", out var phase) ? phase.GetString() : null;
    }
}
