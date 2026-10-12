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

[TestClass]
public sealed class AiAgentToolLoopApprovalResumeTests
{
    [TestMethod]
    public async Task Approved_retry_uses_real_executor_and_preserves_original_identity_and_prefix()
    {
        var f = await Paused(); f.Approval = AgentApprovalExecutionStatus.Approved;
        var result = await new AgentToolLoop().RunAsync(f.Request());
        Assert.AreEqual("done", result.Text); Assert.AreEqual(4, result.ToolSteps);
        Assert.AreEqual(5L, result.InputTokens); Assert.AreEqual(3L, result.OutputTokens);
        Assert.AreEqual(2, JsonNode.Parse(f.Saved!)!["Version"]!.GetValue<int>());
        await f.Write.Received(1).ExecuteAsync(Arg.Is<ToolInvocation>(i => i != null
            && i.OperationId == Operation(f.RunId, "write") && i.RunId == f.RunId && i.ToolVersion == 1
            && i.Arguments.GetRawText() == "{\"title\":\"original\"}"), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await f.Read.Received(2).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await f.Model.Received(2).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        // 最终回执重复领取只交付原结果；没有第二次写入或模型派发。
        await new AgentToolLoop().RunAsync(f.Request());
        await f.Write.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("required")] [DataRow("denied")] [DataRow("permission")] [DataRow("disabled")] [DataRow("version")]
    public async Task Reentry_never_grants_current_approval_or_authority(string fault)
    {
        var f = await Paused(); f.Approval = fault == "required" ? AgentApprovalExecutionStatus.Required
            : fault == "denied" ? AgentApprovalExecutionStatus.Denied : AgentApprovalExecutionStatus.Approved;
        if (fault == "permission") f.Authorized = false;
        if (fault is "disabled" or "version") f.Registry = f.Catalog(fault == "version" ? 2 : 1, fault != "disabled");
        var result = await new AgentToolLoop().RunAsync(f.Request());
        Assert.AreEqual(fault == "required", result.ApprovalRequired);
        Assert.AreEqual(fault == "required" ? "blocked" : "completed", Phase(f.Saved!));
        await f.Write.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(fault == "required" ? 3 : 4, result.ToolSteps);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Unconfirmed_retry_commit_stops_and_unknown_intent_is_never_replayed(bool receiptFailure)
    {
        var f = await Paused(); f.Approval = AgentApprovalExecutionStatus.Approved;
        var original = f.Saved;
        var request = f.Request() with { PersistCheckpoint = (payload, _) =>
        {
            var phase = Phase(payload);
            if (phase == (receiptFailure ? "tools_ready" : "tool_pending")) return Task.FromResult(false);
            f.Saved = payload; return Task.FromResult(true);
        } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(request));
        if (receiptFailure)
        {
            Assert.AreEqual("tool_pending", Phase(f.Saved!));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(f.Request()));
        }
        else Assert.AreEqual(original, f.Saved);
        await f.Write.Received(receiptFailure ? 1 : 0).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await f.Model.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(60)] [DataRow(61)]
    public async Task Approval_retries_share_original_64_step_budget(int retries)
    {
        var f = await Paused(); f.Approval = AgentApprovalExecutionStatus.Approved;
        f.Change(body => { body["Version"] = 2; WriteCall(body)["ApprovalRetries"] = retries; });
        await Assert.ThrowsAsync<AgentToolLoopBudgetExceededException>(() => new AgentToolLoop().RunAsync(f.Request()));
        await f.Write.Received(retries == 60 ? 1 : 0).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await f.Read.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await f.Model.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("negative")] [DataRow("overflow")] [DataRow("v1-count")] [DataRow("v2-zero")]
    [DataRow("operation")] [DataRow("failed-approval")] [DataRow("reconciliation")] [DataRow("nonterminal-pause")]
    public async Task Invalid_or_unknown_pause_never_enters_executor(string fault)
    {
        var f = await Paused();
        f.Change(body =>
        {
            var call = WriteCall(body);
            switch (fault)
            {
                case "negative": body["Version"] = 2; call["ApprovalRetries"] = -1; break;
                case "overflow": body["Version"] = 2; call["ApprovalRetries"] = 64; break;
                case "v1-count": call["ApprovalRetries"] = 1; break;
                case "v2-zero": body["Version"] = 2; break;
                case "operation": call["OperationId"] = Guid.CreateVersion7(); break;
                case "failed-approval": call["ResultPayload"] = Receipt("failed", "ai.tool.approval_required"); break;
                case "reconciliation": call["ResultPayload"] = Receipt("failed", "ai.tool.reconciliation_required"); break;
                case "nonterminal-pause": body["Rounds"]![0]!["Messages"]![0]!["Contents"]![2]!["OperationId"] = Operation(f.RunId, "last");
                    body["Rounds"]![0]!["Messages"]![0]!["Contents"]![2]!["ResultPayload"] = Receipt("succeeded", null); break;
            }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(f.Request()));
        await f.Write.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
        await f.Model.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reentry_resolver_drift_keeps_the_already_frozen_operation()
    {
        var f = await Paused(); f.Approval = AgentApprovalExecutionStatus.Approved;
        var reads = 0; var changed = Guid.CreateVersion7();
        await new AgentToolLoop().RunAsync(f.Request() with { ResolveOperationId = call =>
            call == "write" && ++reads > 1 ? changed : Operation(f.RunId, call) });
        await f.Write.Received(1).ExecuteAsync(Arg.Is<ToolInvocation>(i => i != null
            && i.OperationId == Operation(f.RunId, "write")), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reentry_requires_durable_commit_and_cancelled_reentry_never_dispatches()
    {
        var f = await Paused();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(f.Request() with { PersistCheckpoint = null }));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AgentToolLoop().RunAsync(f.Request(), cancel.Token));
        await f.Write.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>());
    }

    private static string Receipt(string status, string? code) => JsonSerializer.Serialize(new { statusKey = status, errorCode = code, value = true, isUntrusted = true });
    private static JsonNode WriteCall(JsonNode body) => body["Rounds"]![0]!["Messages"]![0]!["Contents"]![1]!;
    private static string Phase(string payload) => JsonNode.Parse(payload)!["Phase"]!.GetValue<string>();
    private static Guid Operation(Guid run, string call) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{run:N}|tool|{call}")).AsSpan(0, 16), bigEndian: true);
    private static async Task<Fixture> Paused()
    {
        var f = new Fixture();
        var result = await new AgentToolLoop().RunAsync(f.Request() with { ResumeCheckpoint = null, RecheckApproval = false });
        Assert.IsTrue(result.ApprovalRequired); Assert.AreEqual(2, result.ToolSteps);
        return f;
    }
    private sealed class Fixture
    {
        public readonly Guid RunId = Guid.CreateVersion7();
        public readonly IChatClient Model = Substitute.For<IChatClient>();
        public readonly IAgentToolHandler Read = Substitute.For<IAgentToolHandler>(), Write = Substitute.For<IAgentToolHandler>();
        public AgentToolRegistry Registry;
        public AgentApprovalExecutionStatus Approval = AgentApprovalExecutionStatus.Required;
        public bool Authorized = true;
        public string? Saved;
        private readonly IAgentToolRegistrySource _source = Substitute.For<IAgentToolRegistrySource>();
        private readonly IToolAuthorizationPort _authorization = Substitute.For<IToolAuthorizationPort>();
        private readonly IAgentApprovalPort _approval = Substitute.For<IAgentApprovalPort>();
        public Fixture()
        {
            Registry = Catalog();
            Read.ValidateArguments(Arg.Any<JsonElement>()).Returns(true); Write.ValidateArguments(Arg.Any<JsonElement>()).Returns(true);
            Read.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>()).Returns(JsonSerializer.SerializeToElement(true));
            Write.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>()).Returns(JsonSerializer.SerializeToElement(true));
            _source.GetRegistryAsync(Arg.Any<CancellationToken>()).Returns(_ => Registry);
            var actor = new ToolActor(Guid.CreateVersion7(), null, Guid.CreateVersion7());
            _authorization.AuthorizeAsync("permission", Arg.Any<CancellationToken>()).Returns(_ => Authorized ? actor : null);
            _approval.ValidateForExecutionAsync(Arg.Any<ToolInvocation>(), Arg.Any<string>(), Arg.Any<ToolActor>(), Arg.Any<CancellationToken>()).Returns(_ => Approval);
            Model.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(
                new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("first", "ai.models.list", new Dictionary<string, object?>()),
                 new FunctionCallContent("write", "ai.tools.ping", new Dictionary<string, object?> { ["title"] = "original" }),
                 new FunctionCallContent("last", "ai.chat.sessions.list", new Dictionary<string, object?>())]))
                    { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } },
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")) { Usage = new() { InputTokenCount = 3, OutputTokenCount = 2 } });
        }
        public AgentToolRegistry Catalog(int version = 1, bool enabled = true) => new(
            [new("ai.models.list", 1, "permission", "read", true, Read),
             new("ai.tools.ping", version, "permission", "write", enabled, Write),
             new("ai.chat.sessions.list", 1, "permission", "read", true, Read)]);
        public AgentToolLoopRequest Request() => new(RunId, AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "hello", Model,
            new AgentToolExecutor(_source, _authorization, Substitute.For<IToolAuditPort>(), _approval), Registry,
            call => Operation(RunId, call), ResumeCheckpoint: Saved,
            PersistCheckpoint: (payload, _) => { Saved = payload; return Task.FromResult(true); }) { RecheckApproval = true };
        public void Change(Action<JsonNode> update) { var body = JsonNode.Parse(Saved!)!; update(body); Saved = body.ToJsonString(); }
    }
}
