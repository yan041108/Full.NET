using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    public async Task Single_model_intent_is_committed_before_runner_dispatch()
    {
        CoordinatorFixture? fixture = null;
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var writes = fixture!.Commands.ReceivedCalls().Where(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.InsertCheckpoint).ToArray();
            Assert.AreEqual(1, writes.Length);
            var values = (IReadOnlyDictionary<string, object?>)writes[0].GetArguments()[1]!;
            using var payload = JsonDocument.Parse((string)values["PayloadProtected"]!);
            Assert.AreEqual("started", payload.RootElement.GetProperty("Phase").GetString());
            return new AgentModelResult("reply", JsonSerializer.SerializeToElement(true), 2, 1);
        });
        fixture = await CreateFixtureAsync(runner: runner);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
    }

    [TestMethod]
    public async Task Single_model_pending_checkpoint_reconciles_without_creating_client_or_settling()
    {
        var fixture = await CreateFixtureAsync(checkpointFactory: run => SingleCheckpoint(run));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Single_model_dispatch_exception_preserves_unknown_outcome_for_reconciliation()
    {
        var fixture = await CreateFixtureAsync(runnerThrows: true);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Single_model_lost_intent_commit_never_dispatches()
    {
        var fixture = await CreateFixtureAsync(commitSucceeds: false);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Single_model_legacy_checkpoint_never_restarts_from_prompt()
    {
        var fixture = await CreateFixtureAsync(checkpointFactory: run => SingleCheckpoint(run, payload: "{\"messages\":[]}"));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
    }

    private static AgentCheckpointRecord SingleCheckpoint(AiAgentRunRecord run, string? payload = null)
    {
        var budget = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot)!;
        payload ??= new JsonObject
        {
            ["PayloadVersion"] = 1, ["RunId"] = run.Id, ["SessionId"] = run.SessionId,
            ["DefinitionKey"] = run.DefinitionKey, ["DefinitionVersion"] = run.DefinitionVersion,
            ["OperationId"] = run.Id, ["ModelConfigId"] = budget.ModelConfigId,
            ["ModelConfigVersion"] = 1, ["BudgetDigest"] = SingleDigest(run.BudgetJson),
            ["InputDigest"] = SingleDigest("hello"), ["Phase"] = "started"
        }.ToJsonString();
        return new(Guid.CreateVersion7(), run.Id, 1, 1, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, payload, SingleDigest(payload));
    }

    [TestMethod]
    [DataRow(2L, 1L)] [DataRow(null, null)] [DataRow(100L, 100L)]
    public async Task Single_model_completed_receipt_finalizes_without_factory_and_preserves_usage(long? input, long? output)
    {
        var fixture = await CreateFixtureAsync(checkpointFactory: run => CompletedSingleCheckpoint(run, input, output));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.Budget.Received(1).SettleAsync(fixture.RunId,
            Arg.Is<AiOperationUsage>(u => u != null && u.InputTokens == input && u.OutputTokens == output), "succeeded", Arg.Any<CancellationToken>());
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any(c => c.GetArguments()[0] is SqlStatement s && s == AiAgentRunSql.InsertStep));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Single_model_session_revocation_keeps_previous_receipt_and_reservation(bool completed)
    {
        var fixture = await CreateFixtureAsync(bindingValid: false,
            checkpointFactory: run => completed ? CompletedSingleCheckpoint(run) : SingleCheckpoint(run));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "authorization_required");
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("model-missing")] [DataRow("model-revision")] [DataRow("prompt-deactivated")] [DataRow("input-digest")]
    public async Task Single_model_completed_receipt_policy_rejection_never_overwrites_known_usage(string fault)
    {
        var fixture = await CreateFixtureAsync(includeModel: fault != "model-missing",
            promptBound: fault == "prompt-deactivated", promptDeactivated: fault == "prompt-deactivated",
            checkpointFactory: run =>
            {
                var checkpoint = CompletedSingleCheckpoint(run);
                var body = JsonNode.Parse(checkpoint.PayloadProtected)!;
                if (fault == "model-revision") body["ModelConfigVersion"] = 2;
                if (fault == "input-digest") body["InputDigest"] = SingleDigest("different prompt");
                var json = body.ToJsonString();
                return checkpoint with { PayloadProtected = json, Checksum = SingleDigest(json) };
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("run")] [DataRow("session")] [DataRow("operation")] [DataRow("model")]
    [DataRow("version")] [DataRow("format")] [DataRow("framework")] [DataRow("checksum")]
    [DataRow("sequence")] [DataRow("sequence-max")] [DataRow("budget")] [DataRow("result")]
    [DataRow("usage-negative")] [DataRow("usage-overrun")] [DataRow("duplicate")]
    [DataRow("escaped-duplicate")] [DataRow("payload-version")] [DataRow("phase")]
    public async Task Single_model_corrupt_checkpoint_never_replays_or_discards_prior_budget(string fault)
    {
        var fixture = await CreateFixtureAsync(checkpointFactory: run =>
        {
            var checkpoint = CompletedSingleCheckpoint(run);
            var body = JsonNode.Parse(checkpoint.PayloadProtected)!;
            switch (fault)
            {
                case "run": body["RunId"] = Guid.CreateVersion7(); break;
                case "session": body["SessionId"] = Guid.CreateVersion7(); break;
                case "operation": body["OperationId"] = Guid.CreateVersion7(); break;
                case "model": body["ModelConfigId"] = Guid.CreateVersion7(); break;
                case "budget": body["BudgetDigest"] = new string('0', 64); break;
                case "result": body["OutputDigest"] = new string('0', 64); break;
                case "usage-negative": body["Result"]!["InputTokens"] = -1; break;
                case "usage-overrun": body["Result"]!["OutputTokens"] = 101; break;
                case "payload-version": body["PayloadVersion"] = 2; break;
                case "phase": body["Phase"] = "unknown"; break;
            }
            var json = body.ToJsonString();
            if (fault == "duplicate") json = json[..^1] + ",\"Phase\":\"started\"}";
            if (fault == "escaped-duplicate") json = json[..^1] + ",\"Ph\\u0061se\":\"started\"}";
            checkpoint = checkpoint with { PayloadProtected = json, Checksum = SingleDigest(json) };
            return fault switch
            {
                "version" => checkpoint with { DefinitionVersion = 999 },
                "format" => checkpoint with { FormatVersion = 999 },
                "framework" => checkpoint with { FrameworkVersion = "unknown" },
                "checksum" => checkpoint with { Checksum = new string('0', 64) },
                "sequence" => checkpoint with { Sequence = 0 },
                "sequence-max" => checkpoint with { Sequence = long.MaxValue },
                _ => checkpoint
            };
        });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Single_model_missing_checkpoint_after_previous_lease_never_replays()
    {
        var fixture = await CreateFixtureAsync(configureRecord: run => run.LeaseEpoch = 2);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Single_model_budget_rejection_distinguishes_local_refusal_from_uncertain_dispatch(bool dispatched)
    {
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<AgentModelResult>>(_ => throw new AiRunOutputBudgetException(dispatched));
        var fixture = await CreateFixtureAsync(runner: runner);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, dispatched ? "reconciliation_required" : "failed");
        if (dispatched)
            await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        else
            await fixture.Budget.Received(1).SettleAsync(fixture.RunId, Arg.Any<AiOperationUsage>(), "failed", Arg.Any<CancellationToken>());
    }

    private static AgentCheckpointRecord CompletedSingleCheckpoint(AiAgentRunRecord run, long? input = 2, long? output = 1)
    {
        var budget = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot)!;
        var state = AiSingleModelCheckpoint.Start(run, new AiModelConfigRecord { Id = budget.ModelConfigId, Version = 1 }, "hello")
            .Complete(new("reply", JsonSerializer.SerializeToElement(true), input, output));
        var json = state.ToJson();
        return new(Guid.CreateVersion7(), run.Id, 2, 1, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, json, SingleDigest(json));
    }

    [TestMethod]
    [DataRow(1L)] [DataRow(2L)]
    public async Task Single_model_checkpoint_read_failure_never_means_no_previous_dispatch(long epoch)
    {
        var fixture = await CreateFixtureAsync(configureRecord: run => run.LeaseEpoch = epoch,
            checkpointFactory: _ => throw new InvalidOperationException("checkpoint read unavailable"));
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "reconciliation_required");
        await fixture.Factory.DidNotReceive().CreateChatClientAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        await fixture.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static string SingleDigest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
