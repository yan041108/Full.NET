using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>派发前重验目录；写工具派发后未知结果必须暂停对账，不能推断未执行。</summary>
[TestClass]
public sealed class AiToolDispatchOutcomeTests
{
    [TestMethod]
    [DataRow("write", "throw")]
    [DataRow("write", "cancel")]
    [DataRow("write", "output")]
    [DataRow("write", "receipt")]
    [DataRow("write", "cancel_receipt")]
    [DataRow("delete", "throw")]
    [DataRow("delete", "cancel")]
    [DataRow("delete", "output")]
    [DataRow("delete", "receipt")]
    [DataRow("delete", "cancel_receipt")]
    [DataRow("publish", "throw")]
    [DataRow("publish", "cancel")]
    [DataRow("publish", "output")]
    [DataRow("publish", "receipt")]
    [DataRow("publish", "cancel_receipt")]
    public async Task Unknown_write_outcome_requires_reconciliation_without_replay(string effect, string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler(outcome, cancellation);
        var fixture = Create(handler, effect);
        if (outcome == "receipt")
            fixture.Audit.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("receipt unavailable"));
        if (outcome == "cancel_receipt")
            fixture.Audit.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return ValueTask.CompletedTask; });
        var result = await fixture.Executor.ExecuteAsync(fixture.Invocation, cancellation.Token);
        Assert.AreEqual("ai.tool.reconciliation_required", result.ErrorCode);
        Assert.AreEqual("failed", result.StatusKey);
        Assert.IsNull(result.Value);
        Assert.AreEqual(1, handler.Calls);
        if (outcome == "cancel_receipt")
            await fixture.Audit.Received(1).CompleteAsync(fixture.Invocation.OperationId, "succeeded", null,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        else if (outcome != "receipt")
            await fixture.Audit.Received(1).CompleteAsync(fixture.Invocation.OperationId, "failed",
                "ai.tool.reconciliation_required", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.AreNotEqual("succeeded", (await fixture.Executor.ExecuteAsync(fixture.Invocation)).StatusKey);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("throw", "failed", "ai.tool.execution_failed")]
    [DataRow("cancel", "cancelled", "ai.tool.cancelled")]
    [DataRow("output", "failed", "ai.tool.output_limit")]
    [DataRow("cancel_receipt", "cancelled", "ai.tool.cancelled")]
    public async Task Read_only_failures_keep_existing_semantics(string outcome, string status, string code)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler(outcome, cancellation);
        var fixture = Create(handler, "read");
        if (outcome == "cancel_receipt") fixture.Audit.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return ValueTask.CompletedTask; });
        var result = await fixture.Executor.ExecuteAsync(fixture.Invocation, cancellation.Token);
        Assert.AreEqual(status, result.StatusKey);
        Assert.AreEqual(code, result.ErrorCode);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("before", 0, 0, 0)]
    [DataRow("registry", 0, 0, 0)]
    [DataRow("authorization", 1, 0, 0)]
    [DataRow("approval", 1, 1, 0)]
    [DataRow("audit", 1, 1, 1)]
    public async Task Cancellation_stops_each_dispatch_boundary(string boundary, int authorizations, int approvals, int intents)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler("success", cancellation);
        var fixture = Create(handler, "write");
        if (boundary == "before") cancellation.Cancel();
        if (boundary == "registry") fixture.Source.GetRegistryAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return fixture.Registry; });
        if (boundary == "authorization") fixture.Authorization.AuthorizeAsync("permission", Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return fixture.Actor; });
        if (boundary == "approval") fixture.Approval.ValidateForExecutionAsync(Arg.Any<ToolInvocation>(), "write",
            fixture.Actor, Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return AgentApprovalExecutionStatus.Approved; });
        if (boundary == "audit") fixture.Audit.BeginAsync(Arg.Any<ToolInvocation>(), "permission", "started", null,
            Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return ValueTask.CompletedTask; });
        var result = await fixture.Executor.ExecuteAsync(fixture.Invocation, cancellation.Token);
        Assert.AreEqual("cancelled", result.StatusKey);
        Assert.AreEqual("ai.tool.cancelled", result.ErrorCode);
        Assert.AreEqual(0, handler.Calls);
        Assert.AreEqual(authorizations, fixture.Authorization.ReceivedCalls().Count());
        Assert.AreEqual(approvals, fixture.Approval.ReceivedCalls().Count());
        Assert.AreEqual(intents, fixture.Audit.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "BeginAsync"));
    }

    [TestMethod]
    [DataRow("removed")]
    [DataRow("disabled")]
    [DataRow("version")]
    [DataRow("permission")]
    [DataRow("effect")]
    [DataRow("missing_handler")]
    [DataRow("fresh_handler")]
    [DataRow("stable")]
    public async Task Current_tool_policy_is_rechecked_after_intent(string change)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler("success", cancellation);
        var replacement = new Handler("success", cancellation);
        var fixture = Create(handler, "write");
        var latest = new AgentToolRegistry(change == "removed" ? [] : [new("test", change == "version" ? 2 : 1,
            change == "permission" ? "new.permission" : "permission", change == "effect" ? "delete" : "write",
            change != "disabled", change == "missing_handler" ? null : change == "fresh_handler" ? replacement : handler)]);
        fixture.Source.GetRegistryAsync(Arg.Any<CancellationToken>()).Returns(fixture.Registry, latest);
        var result = await fixture.Executor.ExecuteAsync(fixture.Invocation);
        var allowed = change is "stable" or "fresh_handler";
        Assert.AreEqual(allowed ? "succeeded" : "denied", result.StatusKey);
        if (!allowed)
        {
            Assert.AreEqual("ai.tool.unavailable", result.ErrorCode);
            Assert.IsNull(result.Value);
            await fixture.Audit.Received(1).CompleteAsync(fixture.Invocation.OperationId, "denied", "ai.tool.unavailable",
                Arg.Is<int>(value => value == 0), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
        Assert.AreEqual(change == "stable" ? 1 : 0, handler.Calls);
        Assert.AreEqual(change == "fresh_handler" ? 1 : 0, replacement.Calls);
    }

    [TestMethod]
    public async Task Production_registry_source_refreshes_remote_policy_in_same_scope()
    {
        using var cancellation = new CancellationTokenSource();
        var remote = Substitute.For<IRemoteAgentToolDefinitionSource>();
        remote.BuildDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(
            new AgentToolDefinition[] { new("remote", 1, "permission", "read", true, new Handler("success", cancellation)) },
            Array.Empty<AgentToolDefinition>());
        var source = new AgentToolRegistrySource(new AgentToolRegistryFactory(null!, null!, null!, null!, remote));
        Assert.IsNotNull((await source.GetRegistryAsync()).Find("remote"));
        Assert.IsNull((await source.GetRegistryAsync()).Find("remote"), "同一作用域不能继续使用撤回前的目录。");
    }

    [TestMethod]
    [DataRow(AgentApprovalExecutionStatus.Required)]
    [DataRow(AgentApprovalExecutionStatus.Denied)]
    [DataRow(AgentApprovalExecutionStatus.NotRequired)]
    public async Task Approval_is_rechecked_after_audit_intent(AgentApprovalExecutionStatus latest)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler("success", cancellation);
        var f = Create(handler, "write");
        f.Approval.ValidateForExecutionAsync(Arg.Any<ToolInvocation>(), "write", f.Actor, Arg.Any<CancellationToken>())
            .Returns(AgentApprovalExecutionStatus.Approved, latest);
        var result = await f.Executor.ExecuteAsync(f.Invocation);
        Assert.AreEqual("denied", result.StatusKey);
        Assert.AreEqual(latest == AgentApprovalExecutionStatus.Denied ? "ai.tool.approval_denied" : "ai.tool.approval_required", result.ErrorCode);
        Assert.AreEqual(0, handler.Calls);
        await f.Audit.Received(1).CompleteAsync(f.Invocation.OperationId, "denied", result.ErrorCode, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_during_final_approval_never_dispatches_handler()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler("success", cancellation);
        var f = Create(handler, "write"); var checks = 0;
        f.Approval.ValidateForExecutionAsync(Arg.Any<ToolInvocation>(), "write", f.Actor, Arg.Any<CancellationToken>())
            .Returns(_ => { if (++checks == 2) cancellation.Cancel(); return AgentApprovalExecutionStatus.Approved; });
        var result = await f.Executor.ExecuteAsync(f.Invocation, cancellation.Token);
        Assert.AreEqual("cancelled", result.StatusKey);
        Assert.AreEqual(0, handler.Calls);
        Assert.AreEqual(2, checks);
    }

    private static Fixture Create(Handler handler, string effect)
    {
        var actor = new ToolActor(Guid.CreateVersion7(), null, Guid.CreateVersion7());
        var registry = new AgentToolRegistry([new("test", 1, "permission", effect, true, handler)]);
        var source = Substitute.For<IAgentToolRegistrySource>();
        source.GetRegistryAsync(Arg.Any<CancellationToken>()).Returns(registry);
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(actor);
        var approval = Substitute.For<IAgentApprovalPort>();
        approval.ValidateForExecutionAsync(Arg.Any<ToolInvocation>(), Arg.Any<string>(), actor, Arg.Any<CancellationToken>())
            .Returns(AgentApprovalExecutionStatus.Approved);
        var audit = Substitute.For<IToolAuditPort>();
        return new(new(source, authorization, audit, approval), source, registry, authorization, approval, audit, actor,
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), "test", 1, JsonSerializer.SerializeToElement(new Dictionary<string, string>())));
    }

    private sealed record Fixture(AgentToolExecutor Executor, IAgentToolRegistrySource Source, AgentToolRegistry Registry,
        IToolAuthorizationPort Authorization, IAgentApprovalPort Approval, IToolAuditPort Audit, ToolActor Actor, ToolInvocation Invocation);

    private sealed class Handler(string outcome, CancellationTokenSource cancellation) : IAgentToolHandler
    {
        internal int Calls;
        public bool ValidateArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Object;
        public ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken token)
        {
            Calls++;
            if (outcome == "throw") throw new IOException("unknown external outcome");
            if (outcome == "cancel") cancellation.Cancel();
            return ValueTask.FromResult(outcome == "output" ? JsonSerializer.SerializeToElement(new string('x', 65537))
                : JsonSerializer.SerializeToElement(true));
        }
    }
}
