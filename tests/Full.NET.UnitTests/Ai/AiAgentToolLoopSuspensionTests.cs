using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>审批和未知回执必须在首个暂停结果处停止，不能继续向模型或同轮后续工具派发。</summary>
[TestClass]
public sealed class AiAgentToolLoopSuspensionTests
{
    [TestMethod]
    [DataRow("ai.tool.approval_required", 1, 1, true)]
    [DataRow("ai.tool.approval_required", 1, 2, true)]
    [DataRow("ai.tool.approval_required", 8, 1, true)]
    [DataRow("ai.tool.approval_required", 8, 2, true)]
    [DataRow("ai.tool.reconciliation_required", 1, 1, true)]
    [DataRow("ai.tool.reconciliation_required", 1, 2, true)]
    [DataRow("ai.tool.reconciliation_required", 8, 1, true)]
    [DataRow("ai.tool.reconciliation_required", 8, 2, true)]
    [DataRow("ai.tool.approval_required", 1, 1, false)]
    [DataRow("ai.tool.approval_required", 1, 2, false)]
    [DataRow("ai.tool.approval_required", 8, 1, false)]
    [DataRow("ai.tool.approval_required", 8, 2, false)]
    [DataRow("ai.tool.reconciliation_required", 1, 1, false)]
    [DataRow("ai.tool.reconciliation_required", 1, 2, false)]
    [DataRow("ai.tool.reconciliation_required", 8, 1, false)]
    [DataRow("ai.tool.reconciliation_required", 8, 2, false)]
    public async Task Suspension_stops_at_original_tool_and_preserves_usage(string reason, int iteration, int position, bool knownUsage)
    {
        var client = Substitute.For<IChatClient>();
        var modelCalls = 0;
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                modelCalls++;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent($"{modelCalls}:1", "ai.tools.ping", new Dictionary<string, object?>()),
                    new FunctionCallContent($"{modelCalls}:2", "ai.models.list", new Dictionary<string, object?>()),
                ])) { Usage = knownUsage ? new UsageDetails { InputTokenCount = 2, OutputTokenCount = 3 } : null };
            });
        var executor = Substitute.For<IAgentToolExecutor>();
        var toolCalls = 0;
        var expectedSteps = (iteration - 1) * 2 + position;
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++toolCalls == expectedSteps
                ? new ToolExecutionResult("denied", null, reason)
                : new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(true), null));
        var registry = new AgentToolRegistry([
            new("ai.tools.ping", 1, "permission", "none", true, null),
            new("ai.models.list", 1, "permission", "read", true, null),
        ]);
        var resolved = new List<string>();
        var result = await new AgentToolLoop().RunAsync(new(
            Guid.CreateVersion7(), AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "prompt", client, executor, registry,
            callId => { resolved.Add(callId); return Guid.CreateVersion7(); }));

        Assert.AreEqual(iteration, modelCalls);
        Assert.AreEqual(expectedSteps, toolCalls);
        Assert.AreEqual(expectedSteps, resolved.Count);
        Assert.AreEqual($"{iteration}:{position}", resolved[^1]);
        Assert.AreEqual(expectedSteps, result.ToolSteps);
        Assert.AreEqual(string.Empty, result.Text);
        Assert.AreEqual(reason == "ai.tool.approval_required", result.ApprovalRequired);
        Assert.AreEqual(reason == "ai.tool.reconciliation_required", result.ReconciliationRequired);
        Assert.AreEqual(knownUsage ? (long?)iteration * 2 : null, result.InputTokens);
        Assert.AreEqual(knownUsage ? (long?)iteration * 3 : null, result.OutputTokens);
        var messages = result.Session.GetProperty("messages");
        Assert.AreEqual("tool", messages[messages.GetArrayLength() - 2].GetProperty("role").GetString());
    }
}
