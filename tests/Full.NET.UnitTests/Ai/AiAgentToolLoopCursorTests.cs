using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Agent Tool Loop Cursor。</summary>
[TestClass]
public sealed class AiAgentToolLoopCursorTests
{
    [TestMethod]
    public async Task Every_external_dispatch_is_preceded_by_intent_and_followed_by_receipt()
    {
        var fixture = Create(); string? saved = null;
        fixture.Client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.AreEqual("model_pending", Phase(saved!));
                return call.ArgAt<IEnumerable<ChatMessage>>(0).Count() == 1 ? Batch() : Final();
            });
        fixture.Executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.AreEqual("tool_pending", Phase(saved!));
            var body = JsonNode.Parse(saved!)!;
            var current = body["Rounds"]![0]!["Messages"]![0]!["Contents"]!.AsArray()
                .First(c => c!["ResultPayload"] is null)!;
            Assert.AreEqual(call.Arg<ToolInvocation>()!.OperationId, current["OperationId"]!.GetValue<Guid>());
            return new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null);
        });
        var result = await new AgentToolLoop().RunAsync(fixture.Request with
            { PersistCheckpoint = (payload, _) => { saved = payload; return Task.FromResult(true); } });
        Assert.AreEqual("completed", Phase(saved!));
        Assert.AreEqual("done", result.Text); Assert.AreEqual(2, result.ToolSteps);
        Assert.AreEqual(5L, result.InputTokens); Assert.AreEqual(3L, result.OutputTokens);
    }

    [TestMethod]
    public async Task Persisted_prefix_survives_lost_ack_and_only_suffix_runs_after_restart()
    {
        var fixture = Create(); string? receipt = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        {
            PersistCheckpoint = (payload, _) =>
            {
                var info = Inspect(payload, fixture.Request);
                if (info.Phase == "tools_ready" && info.ToolSteps == 1)
                { receipt = payload; throw new InvalidOperationException("ack lost after durable receipt"); }
                return Task.FromResult(true);
            }
        }));
        Assert.IsNotNull(receipt);
        await fixture.Executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        var resumed = Create(fixture.Request.RunId);
        resumed.Client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => { Assert.AreEqual(2, call.ArgAt<IEnumerable<ChatMessage>>(0).Count(m => m.Role == ChatRole.Tool)); return Final(); });
        var result = await new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = receipt });
        Assert.AreEqual(2, result.ToolSteps); Assert.AreEqual(5L, result.InputTokens);
        await resumed.Executor.Received(1).ExecuteAsync(Arg.Is<ToolInvocation>(i => i != null && i.OperationId == OperationId(fixture.Request.RunId, "second")), Arg.Any<CancellationToken>());
        await resumed.Client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("model_pending")] [DataRow("tools_ready")] [DataRow("tool_pending")]
    public async Task Unconfirmed_commit_never_advances_to_next_dispatch(string refusedPhase)
    {
        var fixture = Create(); string? last = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        {
            PersistCheckpoint = (payload, _) => { last = payload; return Task.FromResult(Phase(payload) != refusedPhase); }
        }));
        Assert.AreEqual(refusedPhase, Phase(last!));
        await fixture.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await fixture.Client.Received(refusedPhase == "model_pending" ? 0 : 1)
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("model_pending")] [DataRow("tool_pending")]
    public async Task Pending_external_intent_is_never_replayed(string pendingPhase)
    {
        var fixture = Create(); string? saved = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        {
            PersistCheckpoint = (payload, _) => { if (Phase(payload) == pendingPhase) { saved = payload; return Task.FromResult(false); } return Task.FromResult(true); }
        }));
        var resumed = Create(fixture.Request.RunId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = saved }));
        await resumed.Client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await resumed.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Tool_receipt_write_failure_keeps_pending_intent_without_automatic_retry()
    {
        var fixture = Create(); string? durable = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        {
            PersistCheckpoint = (payload, _) =>
            {
                if (Phase(payload) == "tools_ready" && Inspect(payload, fixture.Request).ToolSteps == 1) return Task.FromResult(false);
                durable = payload; return Task.FromResult(true);
            }
        }));
        Assert.AreEqual("tool_pending", Phase(durable!));
        await fixture.Executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with { ResumeCheckpoint = durable }));
    }

    [TestMethod]
    public async Task Final_model_receipt_recovery_returns_without_external_dispatch()
    {
        var fixture = Create(); string? durable = null;
        var original = await new AgentToolLoop().RunAsync(fixture.Request with
            { PersistCheckpoint = (payload, _) => { durable = payload; return Task.FromResult(true); } });
        var resumed = Create(fixture.Request.RunId);
        var result = await new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = durable });
        Assert.AreEqual(original.Text, result.Text); Assert.AreEqual(original.Session.GetRawText(), result.Session.GetRawText());
        Assert.AreEqual(original.InputTokens, result.InputTokens);
        await resumed.Client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await resumed.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        Assert.IsNotNull(AgentToolLoopCheckpoint.ReadCompletedResult(durable!, resumed.Request.RunId,
            resumed.Request.DefinitionKey, 1, "hello", resumed.Request.ResolveOperationId));
    }

    [TestMethod]
    [DataRow("ai.tool.approval_required")] [DataRow("ai.tool.reconciliation_required")]
    public async Task Paused_tool_receipt_remains_blocked_after_restart(string code)
    {
        var fixture = Create(); string? durable = null;
        fixture.Executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult("denied", null, code));
        var result = await new AgentToolLoop().RunAsync(fixture.Request with
            { PersistCheckpoint = (payload, _) => { durable = payload; return Task.FromResult(true); } });
        Assert.AreEqual("blocked", Phase(durable!)); Assert.AreEqual(1, result.ToolSteps);
        Assert.IsTrue(Inspect(durable!, fixture.Request).ReconciliationRequired);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with { ResumeCheckpoint = durable }));
        await fixture.Executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_while_committing_intent_prevents_provider_dispatch()
    {
        var fixture = Create(); using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        { PersistCheckpoint = (_, _) => { cancellation.Cancel(); return Task.FromResult(true); } }, cancellation.Token));
        await fixture.Client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("version")] [DataRow("disabled")] [DataRow("removed")]
    public async Task Changed_tool_catalog_does_not_rebind_remaining_call(string change)
    {
        var fixture = Create(); var receipt = await FirstReceipt(fixture);
        var resumed = Create(fixture.Request.RunId);
        resumed.Client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Final());
        var registry = new AgentToolRegistry(change == "removed" ? [] :
            [new("ai.tools.ping", change == "version" ? 2 : 1, "permission", "none", change != "disabled", null)]);
        var result = await new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = receipt, Registry = registry });
        Assert.AreEqual(2, result.ToolSteps);
        await resumed.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("run")] [DataRow("prompt")] [DataRow("duplicate")] [DataRow("unknown")]
    [DataRow("receipt-gap")] [DataRow("operation")] [DataRow("oversize")]
    public async Task Invalid_checkpoint_is_rejected_before_external_calls(string fault)
    {
        var fixture = Create(); var receipt = await FirstReceipt(fixture); var body = JsonNode.Parse(receipt)!;
        var calls = body["Rounds"]![0]!["Messages"]![0]!["Contents"]!;
        switch (fault)
        {
            case "run": body["RunId"] = Guid.CreateVersion7(); break;
            case "prompt": body["InputDigest"] = new string('0', 64); break;
            case "unknown": body["IgnoreMe"] = true; break;
            case "receipt-gap": calls[1]!["ResultPayload"] = calls[0]!["ResultPayload"]!.DeepClone(); calls[0]!["ResultPayload"] = null; break;
            case "operation": calls[0]!["OperationId"] = Guid.CreateVersion7(); break;
        }
        receipt = body.ToJsonString();
        if (fault == "duplicate") receipt = receipt[..^1] + ",\"Phase\":\"tools_ready\"}";
        if (fault == "oversize") receipt = new string(' ', 1048577);
        var resumed = Create(fixture.Request.RunId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = receipt }));
        await resumed.Client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        await resumed.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Restored_call_ids_remain_unique_across_following_model_rounds()
    {
        var fixture = Create(); var receipt = await FirstReceipt(fixture); var resumed = Create(fixture.Request.RunId);
        resumed.Client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Batch());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = receipt }));
        await resumed.Executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Restart_never_resets_the_shared_model_and_tool_step_budget()
    {
        var fixture = Create(); string? receipt = null;
        fixture.Client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                Enumerable.Range(0, 62).Select(i => (AIContent)new FunctionCallContent("call-" + i, "ai.tools.ping", new Dictionary<string, object?>())).ToList()))
                { Usage = new() { InputTokenCount = 0, OutputTokenCount = 0 } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        { PersistCheckpoint = (payload, _) => { if (Phase(payload) == "tools_ready" && Inspect(payload, fixture.Request).ToolSteps == 61)
            { receipt = payload; return Task.FromResult(false); } return Task.FromResult(true); } }));
        var resumed = Create(fixture.Request.RunId);
        await Assert.ThrowsAsync<AgentToolLoopBudgetExceededException>(() => new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = receipt }));
        await resumed.Executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await resumed.Client.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("null")] [DataRow("unicode")] [DataRow("html")]
    public async Task Handler_success_value_is_durable_and_not_replayed(string kind)
    {
        var fixture = Create(); string? saved = null;
        var handler = Substitute.For<IAgentToolHandler>();
        handler.ValidateArguments(Arg.Any<JsonElement>()).Returns(true);
        // 原工具输出按 UTF-8 限制；JSON 再转义后的字符长度可能更大，不能把合法输出误判为损坏。
        using var valueDocument = JsonDocument.Parse(kind == "null" ? "null" : "\"" +
            (kind == "html" ? new string('<', 64000) : string.Concat(Enumerable.Repeat("😀", 12000))) + "\"");
        handler.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>())
            .Returns(valueDocument.RootElement.Clone());
        var registry = new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, handler)]);
        var source = Substitute.For<IAgentToolRegistrySource>();
        source.GetRegistryAsync(Arg.Any<CancellationToken>()).Returns(registry);
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync("permission", Arg.Any<CancellationToken>())
            .Returns(new ToolActor(Guid.CreateVersion7(), null, Guid.CreateVersion7()));
        var executor = new AgentToolExecutor(source, authorization, Substitute.For<IToolAuditPort>());
        var request = fixture.Request with { Registry = registry, Executor = executor,
            PersistCheckpoint = (payload, _) => { saved = payload; return Task.FromResult(true); } };
        var result = await new AgentToolLoop().RunAsync(request);
        Assert.AreEqual("done", result.Text); Assert.AreEqual("completed", Phase(saved!));
        var body = JsonNode.Parse(saved!)!;
        var receipt = body["Rounds"]![0]!["Messages"]![0]!["Contents"]![0]!["ResultPayload"]!.GetValue<string>();
        using var json = JsonDocument.Parse(receipt);
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(kind == "null" ? JsonValueKind.Null : JsonValueKind.String, json.RootElement.GetProperty("value").ValueKind);
        var resumed = Create(fixture.Request.RunId);
        var recovered = await new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = saved });
        Assert.AreEqual(result.Session.GetRawText(), recovered.Session.GetRawText());
        await handler.Received(2).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await resumed.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        await resumed.Client.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        body["Rounds"]![0]!["Messages"]![0]!["Contents"]![0]!["ResultPayload"] = "{\"statusKey\":\"succeeded\",\"errorCode\":null,\"isUntrusted\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(resumed.Request with { ResumeCheckpoint = body.ToJsonString() }));
    }

    private static async Task<string> FirstReceipt(Fixture fixture)
    {
        string? saved = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request with
        { PersistCheckpoint = (payload, _) => { if (Phase(payload) == "tools_ready" && Inspect(payload, fixture.Request).ToolSteps == 1)
            { saved = payload; return Task.FromResult(false); } return Task.FromResult(true); } }));
        return saved!;
    }
    private static string Phase(string payload) => JsonNode.Parse(payload)!["Phase"]!.GetValue<string>();
    private static AgentToolLoopRecoveryInfo Inspect(string payload, AgentToolLoopRequest request) =>
        AgentToolLoopCheckpoint.Inspect(payload, request.RunId, request.DefinitionKey, 1,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Prompt))), request.ResolveOperationId);
    private static Guid OperationId(Guid run, string call) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{run:N}|tool|{call}")).AsSpan(0, 16), bigEndian: true);
    private static ChatResponse Batch() => new(new ChatMessage(ChatRole.Assistant,
        [new FunctionCallContent("first", "ai.tools.ping", new Dictionary<string, object?>()),
         new FunctionCallContent("second", "ai.tools.ping", new Dictionary<string, object?>())]))
        { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } };
    private static ChatResponse Final() => new(new ChatMessage(ChatRole.Assistant, "done"))
        { Usage = new() { InputTokenCount = 3, OutputTokenCount = 2 } };
    private static Fixture Create(Guid? run = null)
    {
        var id = run ?? Guid.CreateVersion7(); var client = Substitute.For<IChatClient>(); var executor = Substitute.For<IAgentToolExecutor>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Batch(), Final());
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var request = new AgentToolLoopRequest(id, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "hello", client, executor,
            new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]), call => OperationId(id, call));
        return new(request, client, executor);
    }
    private sealed record Fixture(AgentToolLoopRequest Request, IChatClient Client, IAgentToolExecutor Executor);
}
