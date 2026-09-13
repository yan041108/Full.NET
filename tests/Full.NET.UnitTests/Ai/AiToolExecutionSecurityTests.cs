using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>所有拒绝必须发生在 Handler 派发之前，审计失败也不能降级放行。</summary>
[TestClass]
public sealed class AiToolExecutionSecurityTests
{
    [TestMethod]
    public async Task Request_budget_and_output_limit_are_enforced_async()
    {
        var handler = new CountingHandler();
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ToolActor(Guid.NewGuid(), null, Guid.NewGuid()));
        var executor = new AgentToolExecutor(new FixedAgentToolRegistrySource(new AgentToolRegistry([new("test", 1, "permission", "read", true, handler)])), authorization, Substitute.For<IToolAuditPort>());
        using var arguments = JsonDocument.Parse("{}");
        for (var index = 0; index < 8; index++)
            Assert.AreEqual("succeeded", (await executor.ExecuteAsync(new(Guid.CreateVersion7(), null, "test", 1, arguments.RootElement))).StatusKey);
        Assert.AreEqual("denied", (await executor.ExecuteAsync(new(Guid.CreateVersion7(), null, "test", 1, arguments.RootElement))).StatusKey);
        Assert.AreEqual(8, handler.Calls);
        handler.Output = new string('x', 65537);
        executor = new AgentToolExecutor(new FixedAgentToolRegistrySource(new AgentToolRegistry([new("test", 1, "permission", "read", true, handler)])), authorization, Substitute.For<IToolAuditPort>());
        var result = await executor.ExecuteAsync(new(Guid.CreateVersion7(), null, "test", 1, arguments.RootElement));
        Assert.AreEqual("ai.tool.output_limit", result.ErrorCode);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public async Task Cancelled_request_does_not_dispatch_handler_async()
    {
        var handler = new CountingHandler();
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ToolActor(Guid.NewGuid(), null, Guid.NewGuid()));
        var executor = new AgentToolExecutor(new FixedAgentToolRegistrySource(new AgentToolRegistry([new("test", 1, "permission", "none", true, handler)])), authorization, Substitute.For<IToolAuditPort>());
        using var arguments = JsonDocument.Parse("{}");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var result = await executor.ExecuteAsync(new(Guid.CreateVersion7(), null, "test", 1, arguments.RootElement), cancelled.Token);
        Assert.AreEqual("cancelled", result.StatusKey);
        Assert.AreEqual(0, handler.Calls);
    }
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("disabled")]
    [DataRow("missing_handler")]
    [DataRow("version")]
    [DataRow("extra_arguments")]
    [DataRow("unauthorized")]
    [DataRow("write")]
    [DataRow("empty_operation")]
    [DataRow("audit")]
    [DataRow("identity_changed")]
    public async Task Rejection_never_dispatches_handler_async(string scenario)
    {
        var handler = new CountingHandler();
        var actor = new ToolActor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            scenario == "unauthorized" ? null : actor,
            scenario == "identity_changed" ? actor with { TenantId = Guid.NewGuid() } : actor);
        var audit = Substitute.For<IToolAuditPort>();
        if (scenario == "audit") audit.BeginAsync(Arg.Any<ToolInvocation>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("private database detail"));
        var executor = new AgentToolExecutor(new FixedAgentToolRegistrySource(new AgentToolRegistry([new("test", 1, "permission", scenario == "write" ? "write" : "read",
            scenario != "disabled", scenario == "missing_handler" ? null : handler)])), authorization, audit);
        var invocation = new ToolInvocation(scenario == "empty_operation" ? Guid.Empty : Guid.NewGuid(),
            scenario == "agent_run" ? Guid.NewGuid() : null,
            scenario == "unknown" ? "unknown" : "test", scenario == "version" ? 2 : 1,
            JsonSerializer.SerializeToElement(scenario == "extra_arguments" ? new Dictionary<string, string> { ["tenantId"] = "injected" } : []));
        var result = await executor.ExecuteAsync(invocation);
        Assert.AreNotEqual("succeeded", result.StatusKey);
        Assert.AreEqual(0, handler.Calls);
        Assert.IsNull(result.Value);
        Assert.IsTrue(result.IsUntrusted);
        Assert.DoesNotContain("private", result.ErrorCode ?? "", StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Receipt_failure_does_not_publish_success_or_replay_async(bool receiptFails)
    {
        var handler = new CountingHandler();
        var authorization = Substitute.For<IToolAuthorizationPort>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ToolActor(Guid.NewGuid(), null, Guid.NewGuid()));
        var audit = Substitute.For<IToolAuditPort>();
        if (receiptFails) audit.CompleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("receipt failed"));
        var executor = new AgentToolExecutor(new FixedAgentToolRegistrySource(new AgentToolRegistry([new("test", 1, "permission", "none", true, handler)])), authorization, audit);
        var invocation = new ToolInvocation(Guid.NewGuid(), null, "test", 1, JsonSerializer.SerializeToElement(new Dictionary<string, string>()));
        var result = await executor.ExecuteAsync(invocation);
        Assert.AreEqual(receiptFails ? "failed" : "succeeded", result.StatusKey);
        if (receiptFails)
        {
            Assert.AreEqual("ai.tool.reconciliation_required", result.ErrorCode);
        }

        Assert.AreEqual(1, handler.Calls);
        Assert.AreNotEqual("succeeded", (await executor.ExecuteAsync(invocation)).StatusKey);
        Assert.AreEqual(1, handler.Calls);
    }

    private sealed class CountingHandler : IAgentToolHandler
    {
        internal int Calls;
        internal string? Output;
        public bool ValidateArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Object && !arguments.EnumerateObject().Any();
        public ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(Output is null ? JsonSerializer.SerializeToElement(true) : JsonSerializer.SerializeToElement(Output));
        }
    }
}
