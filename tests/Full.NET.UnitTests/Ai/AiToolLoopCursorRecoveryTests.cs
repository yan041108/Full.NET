using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    public async Task Durable_loop_cursor_reuses_completed_prefix_and_executes_only_remaining_call()
    {
        var client = RecoveryLoopClient();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var history = call.ArgAt<IEnumerable<ChatMessage>>(0).ToArray();
                Assert.AreEqual(2, history.Count(m => m.Role == ChatRole.Tool));
                Assert.AreEqual(99, call.ArgAt<ChatOptions>(1).MaxOutputTokens);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                    { Usage = new() { InputTokenCount = 3, OutputTokenCount = 2 } };
            });
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            chatClient: client, configureRecord: run => run.LeaseEpoch = 2,
            checkpointFactory: run => CursorCheckpoint(run));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.ToolExecutor.Received(1).ExecuteAsync(Arg.Is<ToolInvocation>(i =>
            i != null && i.OperationId == CursorOperationId(fixture.RunId, "second")), Arg.Any<CancellationToken>());
        await client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId,
            Arg.Is<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(u => u != null && u.InputTokens == 5 && u.OutputTokens == 3),
            "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("run")] [DataRow("definition")] [DataRow("version")] [DataRow("input")]
    [DataRow("pending-model")] [DataRow("pending-tool")] [DataRow("blocked")]
    [DataRow("negative-usage")] [DataRow("unknown-usage")] [DataRow("exhausted-usage")]
    [DataRow("operation")] [DataRow("duplicate-id")] [DataRow("missing-prefix")]
    [DataRow("unknown-field")] [DataRow("bad-result")] [DataRow("paused-result")]
    public async Task Durable_loop_cursor_invalid_or_uncertain_state_never_creates_client_or_tool(string fault)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: run => CursorCheckpoint(run, loop =>
            {
                var round = loop["Rounds"]![0]!;
                var calls = round["Messages"]![0]!["Contents"]!;
                switch (fault)
                {
                    case "run": loop["RunId"] = Guid.CreateVersion7(); break;
                    case "definition": loop["DefinitionKey"] = "unknown"; break;
                    case "version": loop["Version"] = 999; break;
                    case "input": loop["InputDigest"] = SingleDigest("different"); break;
                    case "pending-model": loop["Phase"] = "model_pending"; break;
                    case "pending-tool": loop["Phase"] = "tool_pending"; calls[1]!["OperationId"] = CursorOperationId(run.Id, "second"); break;
                    case "blocked": loop["Phase"] = "blocked"; break;
                    case "negative-usage": round["InputTokens"] = -1; break;
                    case "unknown-usage": round["OutputTokens"] = null; break;
                    case "exhausted-usage": round["InputTokens"] = 100; break;
                    case "operation": calls[0]!["OperationId"] = Guid.CreateVersion7(); break;
                    case "duplicate-id": calls[1]!["CallId"] = "first"; break;
                    case "missing-prefix": calls[0]!["ResultPayload"] = null; calls[1]!["ResultPayload"] = "{}"; break;
                    case "unknown-field": loop["Unsafe"] = true; break;
                    case "bad-result": calls[0]!["ResultPayload"] = "{}"; break;
                    case "paused-result": calls[0]!["ResultPayload"] = """{"statusKey":"denied","errorCode":"ai.tool.approval_required","value":null,"isUntrusted":true}"""; break;
                }
            }));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<Full.NET.AI.Abstractions.Models.ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(2L, 1L)] [DataRow(null, null)] [DataRow(100L, 100L)]
    public async Task Durable_loop_cursor_final_model_receipt_recovers_without_client_or_tool(long? input, long? output)
    {
        var fixture = await CreateFixtureAsync(definitionKey: AgentDefinitionRegistry.ReadOnlyToolLoopKey,
            checkpointFactory: run => CursorCheckpoint(run, loop =>
            {
                loop["Phase"] = "completed";
                var round = loop["Rounds"]![0]!; round["InputTokens"] = input; round["OutputTokens"] = output;
                round["Messages"]![0]!["Contents"] = new JsonArray(new JsonObject { ["Kind"] = "text", ["Text"] = "done" });
            }));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<Full.NET.AI.Abstractions.Models.ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.ToolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId,
            Arg.Is<Full.NET.AI.Abstractions.Budgets.AiOperationUsage>(u => u != null && u.InputTokens == input && u.OutputTokens == output),
            "succeeded", Arg.Any<CancellationToken>());
    }

    private static Guid CursorOperationId(Guid runId, string callId) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:N}|tool|{callId}")).AsSpan(0, 16), bigEndian: true);

    private static AgentCheckpointRecord CursorCheckpoint(AiAgentRunRecord run, Action<JsonObject>? change = null)
    {
        var source = CompletedSingleCheckpoint(run);
        var envelope = JsonNode.Parse(source.PayloadProtected)!.AsObject();
        var loop = new JsonObject
        {
            ["Version"] = 1, ["RunId"] = run.Id, ["DefinitionKey"] = run.DefinitionKey,
            ["DefinitionVersion"] = run.DefinitionVersion, ["InputDigest"] = SingleDigest("hello"),
            ["Phase"] = "tools_ready",
            ["Rounds"] = new JsonArray(new JsonObject
            {
                ["InputTokens"] = 2, ["OutputTokens"] = 1,
                ["Messages"] = new JsonArray(new JsonObject
                {
                    ["Contents"] = new JsonArray(
                        new JsonObject { ["Kind"] = "call", ["CallId"] = "first", ["Name"] = "ai.tools.ping",
                            ["Arguments"] = new JsonObject(), ["ToolVersion"] = 1,
                            ["OperationId"] = CursorOperationId(run.Id, "first"),
                            ["ResultPayload"] = """{"statusKey":"succeeded","errorCode":null,"value":true,"isUntrusted":true}""" },
                        new JsonObject { ["Kind"] = "call", ["CallId"] = "second", ["Name"] = "ai.tools.ping",
                            ["Arguments"] = new JsonObject(), ["ToolVersion"] = 1 })
                })
            })
        };
        change?.Invoke(loop);
        envelope["PayloadVersion"] = 2; envelope["Phase"] = "loop_ready";
        envelope["Result"] = null; envelope["OutputDigest"] = null;
        envelope["LoopState"] = loop.ToJsonString();
        var json = envelope.ToJsonString();
        return source with { PayloadProtected = json, Checksum = SingleDigest(json) };
    }
}
