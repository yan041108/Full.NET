using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>模型与每个工具请求共用步数额度；拒绝项也不能为循环免费增加工作量。</summary>
[TestClass]
public sealed class AiAgentToolLoopStepBudgetTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Single_large_response_is_rejected_before_any_tool(bool denied)
    {
        var fixture = Fixture([Calls(65, denied ? "unknown.tool" : "ai.tools.ping"), Text()]);
        await Assert.ThrowsAsync<AgentToolLoopBudgetExceededException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(1, fixture.ModelCalls());
        Assert.AreEqual(0, fixture.Resolved.Count);
        await fixture.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Step_budget_is_shared_across_model_rounds_and_tools()
    {
        var fixture = Fixture([Calls(40), Calls(24, prefix: "second"), Text()]);
        await Assert.ThrowsAsync<AgentToolLoopBudgetExceededException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(2, fixture.ModelCalls());
        Assert.AreEqual(40, fixture.Resolved.Count);
        await fixture.Executor.Received(40).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Final_response_at_exact_step_64_is_allowed()
    {
        var fixture = Fixture([Calls(62), Text()]);
        var result = await new AgentToolLoop().RunAsync(fixture.Request);
        Assert.AreEqual("done", result.Text);
        Assert.AreEqual(62, result.ToolSteps);
        Assert.AreEqual(2, fixture.ModelCalls());
    }

    [TestMethod]
    public async Task Exhausted_steps_never_dispatch_next_model()
    {
        var fixture = Fixture([Calls(63), Text()]);
        await Assert.ThrowsAsync<AgentToolLoopBudgetExceededException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(1, fixture.ModelCalls());
        Assert.AreEqual(63, fixture.Resolved.Count);
    }

    [TestMethod]
    [DataRow("ai.tool.approval_required")] [DataRow("ai.tool.reconciliation_required")]
    public async Task Pause_on_last_allowed_step_preserves_pause_without_next_model(string code)
    {
        var fixture = Fixture([Calls(63), Text()]);
        var executed = 0;
        fixture.Executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(_ => ++executed == 63
            ? new ToolExecutionResult("denied", null, code) : new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var result = await new AgentToolLoop().RunAsync(fixture.Request);
        Assert.AreEqual(63, result.ToolSteps);
        Assert.AreEqual(code == "ai.tool.approval_required", result.ApprovalRequired);
        Assert.AreEqual(code == "ai.tool.reconciliation_required", result.ReconciliationRequired);
        Assert.AreEqual(1, fixture.ModelCalls());
    }

    private static (AgentToolLoopRequest Request, IAgentToolExecutor Executor, List<string> Resolved, Func<int> ModelCalls) Fixture(ChatResponse[] responses)
    {
        var client = Substitute.For<IChatClient>(); var modelCalls = 0;
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => responses[Math.Min(modelCalls++, responses.Length - 1)]);
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var resolved = new List<string>();
        return (new(Guid.CreateVersion7(), AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "bounded", client, executor,
            new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null)]),
            id => { resolved.Add(id); return Guid.CreateVersion7(); }), executor, resolved, () => modelCalls);
    }

    private static ChatResponse Calls(int count, string tool = "ai.tools.ping", string prefix = "call") => new(new ChatMessage(ChatRole.Assistant,
        Enumerable.Range(0, count).Select(i => (AIContent)new FunctionCallContent($"{prefix}-{i}", tool, new Dictionary<string, object?>())).ToList()));

    private static ChatResponse Text() => new(new ChatMessage(ChatRole.Assistant, "done"));
}
