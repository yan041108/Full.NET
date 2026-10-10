using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)]
    public async Task Rejected_node_commit_stops_at_the_exact_durable_boundary(int rejectedCommit)
    {
        var commits = 0;
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, runner: SuccessfulWorkflowRunner(),
            configureCommands: commands => commands.ExecuteAsync(AiAgentRunSql.UpdateRunStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => ++commits == rejectedCommit ? 0 : 1));
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(rejectedCommit, commits);
        Assert.AreEqual((rejectedCommit - 1) / 2, Parameters(fixture.Commands, AiAgentRunSql.InsertStep).Count);
        var dispatched = rejectedCommit / 2;
        Assert.AreEqual(Math.Min(2, Math.Max(0, dispatched - 1)), fixture.Runner.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "RunAsync"));
        Assert.AreEqual(dispatched >= 4 ? 2 : dispatched >= 1 ? 1 : 0, fixture.ToolExecutor.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "ExecuteAsync"));
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("run")] [DataRow("definition")] [DataRow("prompt-digest")]
    [DataRow("prefix-low")] [DataRow("prefix-high")] [DataRow("missing")]
    [DataRow("node-key")] [DataRow("node-index")] [DataRow("operation")]
    [DataRow("input-digest")] [DataRow("output-digest")] [DataRow("tool-version")]
    [DataRow("usage-negative")] [DataRow("pending-output")] [DataRow("null-receipts")]
    public async Task Corrupted_node_proof_fails_before_client_creation(string fault)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = ReceiptState(run, 2, pending: fault == "pending-output");
                state.Binding = fault switch
                {
                    "run" => state.Binding! with { RunId = Guid.CreateVersion7() },
                    "definition" => state.Binding! with { WorkflowVersion = 999 },
                    "prompt-digest" => state.Binding! with { DefinitionDigest = new('0', 64) },
                    "prefix-low" => state.Binding! with { LegacyPrefixCount = -1 },
                    "prefix-high" => state.Binding! with { LegacyPrefixCount = 3 },
                    _ => state.Binding,
                };
                if (fault == "missing") state.NodeReceipts.RemoveAt(0);
                else if (fault == "null-receipts") state.NodeReceipts = null!;
                else if (fault == "pending-output") state.PendingNode = state.PendingNode! with { OutputDigest = "fake" };
                else state.NodeReceipts[0] = fault switch
                {
                    "node-key" => state.NodeReceipts[0] with { NodeKey = "rename" },
                    "node-index" => state.NodeReceipts[0] with { NodeIndex = 1 },
                    "operation" => state.NodeReceipts[0] with { OperationId = Guid.CreateVersion7() },
                    "input-digest" => state.NodeReceipts[0] with { InputDigest = new('0', 64) },
                    "output-digest" => state.NodeReceipts[0] with { OutputDigest = new('0', 64) },
                    "tool-version" => state.NodeReceipts[0] with { ToolVersion = null },
                    "usage-negative" => state.NodeReceipts[0] with { InputTokens = -1 },
                    _ => state.NodeReceipts[0],
                };
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        Assert.AreEqual(0, fixture.Factory.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CreateChatClientAsync"));
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1", false)] [DataRow("fullnet-single-text-v1", true)]
    [DataRow("fullnet-readonly-tool-loop-v1", false)] [DataRow("fullnet-readonly-tool-loop-v1", true)]
    [DataRow("fullnet-chat-rename-workflow-v1", false)] [DataRow("fullnet-chat-rename-workflow-v1", true)]
    public async Task Resumed_rejection_advances_existing_event_without_checkpoint(string definition, bool sessionInvalid)
    {
        var fixture = await CreateFixtureAsync(definitionKey: definition, bindingValid: !sessionInvalid,
            includeModel: sessionInvalid, priorEventSequence: 7);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        Assert.AreEqual(8L, Parameters(fixture.Commands, AiAgentRunSql.InsertEvent).Single()["Sequence"]);
    }

    [TestMethod]
    public async Task Tool_loop_repeated_approval_wait_advances_existing_event()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call", "ai.tools.ping", new Dictionary<string, object?>())])));
        var executor = Substitute.For<Full.NET.Agents.Tools.IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(new Full.NET.AI.Abstractions.Tools.ToolExecutionResult("denied", null, "ai.tool.approval_required"));
        var fixture = await CreateFixtureAsync(definitionKey: "fullnet-readonly-tool-loop-v1", chatClient: client,
            toolExecutor: executor, priorEventSequence: 7);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "awaiting_approval");
        Assert.AreEqual(8L, Parameters(fixture.Commands, AiAgentRunSql.InsertEvent).Single()["Sequence"]);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Dispatched_model_exception_keeps_pending_intent_and_requires_reconciliation(bool independentTimeout)
    {
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<AgentModelResult>>(_ =>
            throw (independentTimeout ? new OperationCanceledException("provider timeout") : (Exception)new HttpRequestException("unknown provider outcome")));
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, runner: runner);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var state = AgentWorkflowState.FromJson((string)Parameters(fixture.Commands, AiAgentRunSql.InsertCheckpoint).Last()["PayloadProtected"]!);
        Assert.AreEqual("summarize", state!.PendingNode!.NodeKey);
        Assert.AreEqual(1, state.NodeReceipts.Count);
    }

    [TestMethod]
    public async Task Rejected_validation_persists_known_receipt_and_never_reuses_event_sequence()
    {
        var runner = SuccessfulWorkflowRunner();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AgentModelResult(call.ArgAt<string>(1).Contains("session list", StringComparison.Ordinal) ? "Title" : """{"decision":"rejected"}""", JsonSerializer.SerializeToElement(true), 2, 1));
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, runner: runner);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        var sequences = Parameters(fixture.Commands, AiAgentRunSql.InsertEvent).Select(p => (long)p["Sequence"]!).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, sequences.Length).Select(v => (long)v).ToArray(), sequences);
        var steps = Parameters(fixture.Commands, AiAgentRunSql.InsertStep);
        Assert.AreEqual(3, steps.Count);
        Assert.AreEqual("validate", steps.Last()["StepKey"]);
        Assert.AreEqual(2L, steps.Last()["InputTokens"]);
        var state = AgentWorkflowState.FromJson((string)Parameters(fixture.Commands, AiAgentRunSql.InsertCheckpoint).Last()["PayloadProtected"]!);
        Assert.IsNull(state!.PendingNode);
        Assert.AreEqual(3, state.NodeReceipts.Count);
        Assert.AreEqual(4L, state.InputTokens);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public async Task Recovery_of_pending_node_never_creates_client_or_dispatches(int index)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run => Checkpoint(run, ReceiptState(run, index, pending: true)));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        Assert.AreEqual(0, fixture.Factory.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CreateChatClientAsync"));
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task Receipt_checkpoint_reuses_completed_prefix_without_redispatch(int index)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, runner: SuccessfulWorkflowRunner(),
            checkpointFactory: run => Checkpoint(run, ReceiptState(run, index)));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        Assert.AreEqual(Math.Max(0, 2 - Math.Max(0, index - 1)), fixture.Runner.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "RunAsync"));
        Assert.AreEqual(index == 0 ? 2 : index < 4 ? 1 : 0, fixture.ToolExecutor.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "ExecuteAsync"));
        Assert.AreEqual(5 - index, Parameters(fixture.Commands, AiAgentRunSql.InsertStep).Count);
    }

    [TestMethod]
    [DataRow("model-id")] [DataRow("model-version")]
    public async Task Changed_model_binding_rejects_before_client_creation(string fault)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = ReceiptState(run, 2);
                state.Binding = fault == "model-id" ? state.Binding! with { ModelConfigId = Guid.CreateVersion7() }
                    : state.Binding! with { ModelConfigVersion = 2 };
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "failed");
        Assert.AreEqual(0, fixture.Factory.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "CreateChatClientAsync"));
    }

    private static AgentWorkflowState ReceiptState(AiAgentRunRecord run, int next, bool pending = false)
    {
        var state = State(run.SessionId, next);
        var definition = AgentWorkflowRegistry.Resolve(run.DefinitionKey, run.DefinitionVersion)!;
        using var budget = JsonDocument.Parse(run.BudgetJson);
        state.Binding = new(run.Id, run.DefinitionKey, run.DefinitionVersion, AgentWorkflowNodeReceipt.DigestDefinition(definition),
            budget.RootElement.GetProperty("ModelConfigId").GetGuid(), 1);
        for (var i = 0; i < next + (pending ? 1 : 0); i++)
        {
            var node = definition.Nodes[i];
            var receipt = new AgentWorkflowNodeReceipt(i, node.Key, AgentWorkflowNodeReceipt.ResolveOperationId(run.Id, node.Key),
                AgentWorkflowNodeReceipt.Digest(AgentWorkflowNodeReceipt.BuildInput(node, State(run.SessionId, i))),
                i == next ? null : AgentWorkflowNodeReceipt.Digest(state.Outputs[node.Key]), node.ToolName is null ? null : 1, null, null);
            if (i == next) state.PendingNode = receipt;
            else state.NodeReceipts.Add(receipt);
        }
        return state;
    }

    [TestMethod]
    public async Task Workflow_persists_each_node_intent_before_dispatch_and_completion_before_next_node()
    {
        var runner = SuccessfulWorkflowRunner();
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, runner: runner);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        var checkpoints = Parameters(fixture.Commands, AiAgentRunSql.InsertCheckpoint);
        Assert.AreEqual(9, checkpoints.Count);
        for (var index = 0; index < 4; index++)
        {
            using var intent = JsonDocument.Parse((string)checkpoints[index * 2]["PayloadProtected"]!);
            using var completed = JsonDocument.Parse((string)checkpoints[index * 2 + 1]["PayloadProtected"]!);
            Assert.AreEqual(index, intent.RootElement.GetProperty("NextNodeIndex").GetInt32());
            Assert.AreEqual(index, intent.RootElement.GetProperty("PendingNode").GetProperty("NodeIndex").GetInt32());
            Assert.AreEqual(index + 1, completed.RootElement.GetProperty("NextNodeIndex").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, completed.RootElement.GetProperty("PendingNode").ValueKind);
            Assert.AreEqual(index + 1, completed.RootElement.GetProperty("NodeReceipts").GetArrayLength());
        }
        var steps = Parameters(fixture.Commands, AiAgentRunSql.InsertStep);
        CollectionAssert.AreEqual(new[] { "read_sessions", "summarize", "validate", "rename", "workflow.complete" }, steps.Select(s => (string)s["StepKey"]!).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(2, 9).Select(v => (long)v).ToArray(), Parameters(fixture.Commands, AiAgentRunSql.UpdateRunStatus).Select(s => (long)s["Version"]!).ToArray());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Failed_node_intent_commit_never_dispatches_any_node_or_settles()
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey, runner: SuccessfulWorkflowRunner(), commitSucceeds: false);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Full.NET.AI.Abstractions.Tools.ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Expired_workflow_scan_requires_durable_node_event_and_checkpoint(bool mysql)
    {
        var sql = (mysql ? AiAgentRunSql.ScanQueuedMySql : AiAgentRunSql.ScanQueuedSqlServer).Text;
        StringAssert.Contains(sql, "StatusKey = 'running'");
        StringAssert.Contains(sql, "fn_ai_agent_checkpoint");
        StringAssert.Contains(sql, "run.node_started");
        StringAssert.Contains(sql, "@WorkflowKey");
    }

    private static IAgentModelRunner SuccessfulWorkflowRunner()
    {
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AgentModelResult(call.ArgAt<string>(1).Contains("session list", StringComparison.Ordinal) ? "Title" : """{"decision":"approved"}""", JsonSerializer.SerializeToElement(true), 2, 1));
        return runner;
    }

    private static List<IReadOnlyDictionary<string, object?>> Parameters(ICommandExecutor commands, SqlStatement sql) =>
        commands.ReceivedCalls().Where(c => c.GetArguments()[0] is SqlStatement statement && statement == sql)
            .Select(c => Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(c.GetArguments()[1])).ToList();
}
