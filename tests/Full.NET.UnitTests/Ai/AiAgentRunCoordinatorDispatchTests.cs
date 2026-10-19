using System.Text.Json;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiAgentRunCoordinator。</summary>
public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, 7L, 7)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, 37L, 37)]
    [DataRow(AgentDefinitionRegistry.ReadOnlyToolLoopKey, 10000L, 4096)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, 7L, 7)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, 37L, 37)]
    [DataRow(AgentWorkflowRegistry.ChatRenameWorkflowKey, 10000L, 4096)]
    public async Task Every_model_dispatch_observes_frozen_output_limit(string definition, long limit, int expected)
    {
        var client = Substitute.For<IChatClient>();
        var dispatched = new List<int?>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched.Add(call.ArgAt<ChatOptions?>(1)?.MaxOutputTokens);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
                    { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } };
            });
        var runner = Substitute.For<IAgentModelRunner>();
        runner.RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await call.ArgAt<IChatClient>(0).GetResponseAsync([new(ChatRole.User, call.ArgAt<string>(1))], cancellationToken: call.ArgAt<CancellationToken>(2));
            var output = call.ArgAt<string>(1).Contains("session list", StringComparison.OrdinalIgnoreCase)
                ? "New Title" : """{"decision":"approved"}""";
            return new AgentModelResult(output, JsonSerializer.SerializeToElement(new { messages = Array.Empty<string>() }), 2, 1);
        });
        var fixture = await CreateFixtureAsync(definitionKey: definition, chatClient: client, runner: runner,
            configureRecord: record =>
            {
                var budget = JsonSerializer.Deserialize(record.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot)!;
                record.BudgetJson = JsonSerializer.Serialize(budget with { OutputTokenLimit = limit }, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        AssertStatusCommit(fixture.Commands, "completed");
        Assert.AreEqual(definition == AgentWorkflowRegistry.ChatRenameWorkflowKey ? 2 : 1, dispatched.Count);
        var expectedLimits = definition == AgentWorkflowRegistry.ChatRenameWorkflowKey
            ? new int?[] { expected, (int)Math.Min(limit - 1, 4096) } : [expected];
        CollectionAssert.AreEqual(expectedLimits, dispatched);
        client.Received(1).Dispose();
    }
}
