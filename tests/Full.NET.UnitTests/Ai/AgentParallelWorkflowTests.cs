using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>通过真实静态图验证并发、持久汇合、父预算及审批边界。</summary>
[TestClass]
public sealed class AgentParallelWorkflowTests
{
    [TestMethod]
    public async Task Parallel_roles_run_two_at_once_then_join_and_stop_at_original_approval()
    {
        var f = new Fixture();
        var allocations = new List<AgentWorkflowModelBudget>();
        var result = await new AgentWorkflowRunner().RunAsync(f.Request(allocations: allocations));
        Assert.AreEqual(AgentWorkflowRunStatus.AwaitingApproval, result.Status);
        Assert.AreEqual(2, f.MaximumActive);
        Assert.AreEqual(0, f.Active);
        Assert.AreEqual(6, f.ModelCalls.Count);
        Assert.AreEqual(4, f.ModelCalls.Count(prompt => prompt.Contains("reviewer:", StringComparison.Ordinal)));
        Assert.AreEqual(1, f.Tools.Count(call => call.ToolName == "ai.chat.sessions.metadata"));
        Assert.AreEqual(1, f.Tools.Count(call => call.ToolName == "ai.chat.sessions.rename"));
        var write = f.Tools.Single(call => call.ToolName == "ai.chat.sessions.rename");
        Assert.AreEqual(f.Target, write.Arguments.GetProperty("sessionId").GetGuid());
        Assert.AreNotEqual(f.State.SessionId, f.Target);
        Assert.AreEqual("Proposed title", write.Arguments.GetProperty("title").GetString());
        CollectionAssert.AreEqual(new long[] { 50, 50, 48, 48, 92, 90 }, allocations.Select(item => item.InputTokenLimit).ToArray());
        CollectionAssert.AreEqual(new long[] { 50, 50, 49, 49, 96, 95 }, allocations.Select(item => item.OutputTokenLimit).ToArray());
        Assert.AreEqual(12L, f.State.InputTokens); Assert.AreEqual(6L, f.State.OutputTokens);
        Assert.AreEqual(7, f.State.NextNodeIndex);
        Assert.AreEqual(8, f.State.ExecutionSteps, "审批拒绝也消耗父运行步骤。");
        Assert.AreEqual(0, f.State.ParallelPendingNodes.Count);
        Assert.IsNull(f.State.PendingNode);
        Assert.IsTrue(f.IntentBarrierObserved);
        Assert.IsNotNull(f.Restore(f.State));
    }

    [TestMethod]
    public async Task Restart_between_waves_keeps_completed_roles_and_their_original_operation_ids()
    {
        var f = new Fixture(); string? saved = null;
        var first = await new AgentWorkflowRunner().RunAsync(f.Request(persist: (receipt, state, _) =>
        {
            if (state.NextNodeIndex == 3 && state.ParallelPendingNodes.Count == 0)
            { saved = state.ToJson(); return Task.FromResult(false); }
            return Task.FromResult(true);
        }));
        Assert.AreEqual(AgentWorkflowRunStatus.ReconciliationRequired, first.Status);
        Assert.IsNotNull(saved);
        f.State = AgentWorkflowState.FromJson(saved)!;
        Assert.IsNotNull(f.Restore(f.State));
        var original = f.State.NodeReceipts.Select(item => item.OperationId).ToArray();
        Assert.IsTrue(AiRunOutputBudgetAdmission.TryGetConsumedTokens(f.Budget(), f.State, out var input, out var output));
        Assert.AreEqual(4L, input); Assert.AreEqual(2L, output);
        var second = await new AgentWorkflowRunner().RunAsync(f.Request());
        Assert.AreEqual(AgentWorkflowRunStatus.AwaitingApproval, second.Status);
        Assert.AreEqual(6, f.ModelCalls.Count, "恢复不能重跑第一波两个角色。");
        CollectionAssert.AreEqual(original, f.State.NodeReceipts.Take(3).Select(item => item.OperationId).ToArray());
        Assert.AreEqual(1, f.Tools.Count(call => call.ToolName == "ai.chat.sessions.metadata"));
    }

    [TestMethod]
    public async Task Partial_wave_requires_all_partner_intents_and_never_reissues_unknown_call()
    {
        var f = new Fixture(); string? partial = null;
        await new AgentWorkflowRunner().RunAsync(f.Request(persist: (receipt, state, _) =>
        {
            if (partial is null && receipt.ToolVersion is null && receipt.OutputDigest is not null)
                partial = state.ToJson();
            return Task.FromResult(partial is null);
        }));
        Assert.IsNotNull(partial);
        f.State = AgentWorkflowState.FromJson(partial)!;
        Assert.IsNotNull(f.Restore(f.State));
        Assert.AreEqual(1, f.State.ParallelPendingNodes.Count);
        var calls = f.ModelCalls.Count;
        var stopped = await new AgentWorkflowRunner().RunAsync(f.Request());
        Assert.AreEqual(AgentWorkflowRunStatus.ReconciliationRequired, stopped.Status);
        Assert.AreEqual(calls, f.ModelCalls.Count);
        f.State.ParallelPendingNodes.Clear();
        Assert.IsNull(f.Restore(f.State), "伙伴意图缺失不能冒充尚未派发。");
    }

    [TestMethod]
    public async Task Root_budget_exhaustion_stops_before_second_wave_and_before_write()
    {
        var f = new Fixture();
        var result = await new AgentWorkflowRunner().RunAsync(f.Request(inputLimit: 5));
        Assert.AreEqual(AgentWorkflowRunStatus.Failed, result.Status);
        Assert.AreEqual("ai.budget.limit_exceeded", result.ErrorCode);
        Assert.AreEqual(2, f.ModelCalls.Count);
        Assert.IsFalse(f.Tools.Any(call => call.ToolName == "ai.chat.sessions.rename"));
        Assert.AreEqual(3, f.State.ExecutionSteps);
    }

    [TestMethod]
    public async Task Parent_step_budget_cannot_be_reset_by_parallel_branch_creation()
    {
        var f = new Fixture(); f.State.ExecutionSteps = 63;
        var result = await new AgentWorkflowRunner().RunAsync(f.Request());
        Assert.AreEqual("ai.agent_run.tool_budget_exceeded", result.ErrorCode);
        Assert.AreEqual(0, f.ModelCalls.Count);
        Assert.AreEqual(64, f.State.ExecutionSteps);
        Assert.IsFalse(f.Tools.Any(call => call.ToolName == "ai.chat.sessions.rename"));
    }

    [TestMethod]
    public async Task Failure_cancels_and_joins_other_roles_without_crossing_approval_write()
    {
        var f = new Fixture { FailRole = true };
        var result = await new AgentWorkflowRunner().RunAsync(f.Request());
        Assert.AreEqual(AgentWorkflowRunStatus.ReconciliationRequired, result.Status);
        Assert.AreEqual(0, f.Active);
        Assert.IsFalse(f.Tools.Any(call => call.ToolName == "ai.chat.sessions.rename"));
        Assert.IsTrue(f.State.ParallelPendingNodes.Count > 0);
    }

    [TestMethod]
    public async Task Cancellation_ends_all_started_roles_without_dispatching_write()
    {
        var f = new Fixture(); using var cancel = new CancellationTokenSource();
        f.DuringRole = () => cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AgentWorkflowRunner().RunAsync(f.Request(), cancel.Token));
        Assert.AreEqual(0, f.Active);
        Assert.IsFalse(f.Tools.Any(call => call.ToolName == "ai.chat.sessions.rename"));
    }

    [TestMethod]
    [DataRow("peer")] [DataRow("write")] [DataRow("width")] [DataRow("reuse")]
    public void Static_definition_rejects_peer_dependencies_write_branches_and_unbounded_or_reused_groups(string fault)
    {
        var nodes = Enumerable.Range(0, fault == "width" ? 5 : 2)
            .Select(i => new AgentWorkflowNode("role" + i, AgentWorkflowNodeKind.ModelText, PromptTemplate: "Review", ParallelGroup: "roles")).ToList();
        if (fault == "peer") nodes[1] = nodes[1] with { PromptTemplate = "Use {{role0}}" };
        if (fault == "write") nodes[1] = nodes[1] with { Kind = AgentWorkflowNodeKind.ToolWrite, ToolName = "write" };
        if (fault == "reuse") nodes.AddRange([new("middle", AgentWorkflowNodeKind.ModelText, PromptTemplate: "Join"),
            new("later", AgentWorkflowNodeKind.ModelText, PromptTemplate: "Review", ParallelGroup: "roles")]);
        Assert.ThrowsExactly<ArgumentException>(() => new AgentWorkflowDefinition("fixture", 1, nodes));
    }

    [TestMethod]
    public async Task Altered_allocation_or_usage_total_is_rejected_before_recovery()
    {
        var f = new Fixture(); await new AgentWorkflowRunner().RunAsync(f.Request());
        Assert.IsTrue(AiRunOutputBudgetAdmission.TryGetConsumedTokens(f.Budget(), f.State, out _));
        var role = f.State.NodeReceipts.FindIndex(receipt => receipt.NodeKey == "review_accuracy");
        var original = f.State.NodeReceipts[role];
        f.State.NodeReceipts[role] = original with { InputTokenLimit = original.InputTokenLimit + 1 };
        Assert.IsFalse(AiRunOutputBudgetAdmission.TryGetConsumedTokens(f.Budget(), f.State, out _));
        f.State.NodeReceipts[role] = original;
        f.State.InputTokens--;
        Assert.IsFalse(AiRunOutputBudgetAdmission.TryGetConsumedTokens(f.Budget(), f.State, out _));
    }

    private sealed class Fixture : IAgentModelRunner
    {
        internal readonly Guid Run = Guid.CreateVersion7(), Target = Guid.CreateVersion7(), Model = Guid.CreateVersion7();
        internal AgentWorkflowState State;
        internal readonly ConcurrentQueue<string> ModelCalls = new();
        internal readonly List<ToolInvocation> Tools = [];
        internal int Active, MaximumActive;
        internal bool FailRole, IntentBarrierObserved = true;
        internal Action? DuringRole;
        internal Fixture()
        {
            State = AgentWorkflowState.Create(Guid.CreateVersion7()); State.TargetChatSessionId = Target;
            State.Binding = new(Run, AgentWorkflowRegistry.ChatReviewWorkflowKey, 1, AgentWorkflowNodeReceipt.DigestDefinition(Definition), Model, 1);
        }
        private static AgentWorkflowDefinition Definition => AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatReviewWorkflowKey, 1)!;
        internal AgentRunBudgetSnapshot Budget() => new(Model, "", 100, 100, TargetChatSessionId: Target);
        internal AgentWorkflowState? Restore(AgentWorkflowState state)
        {
            var json = state.ToJson();
            var run = new AiAgentRunRecord { Id = Run, SessionId = state.SessionId, DefinitionKey = AgentWorkflowRegistry.ChatReviewWorkflowKey, DefinitionVersion = 1 };
            return AiWorkflowCheckpointAdmission.Restore(run, new(Guid.CreateVersion7(), Run, 1, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
                AgentFrameworkRuntime.FrameworkVersion, 1, json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))));
        }
        internal AgentWorkflowRunRequest Request(Func<AgentWorkflowNodeReceipt, AgentWorkflowState, CancellationToken, Task<bool>>? persist = null,
            List<AgentWorkflowModelBudget>? allocations = null, long inputLimit = 100)
        {
            var executor = Substitute.For<IAgentToolExecutor>();
            executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var invocation = call.Arg<ToolInvocation>()!; Tools.Add(invocation);
                return invocation.ToolName == "ai.chat.sessions.metadata"
                    ? new ToolExecutionResult("succeeded", JsonSerializer.SerializeToElement(new { sessionId = Target, title = "Original", version = "1" }), null)
                    : new ToolExecutionResult("denied", null, "ai.tool.approval_required");
            });
            return new(Run, AgentWorkflowRegistry.ChatReviewWorkflowKey, 1, State, Substitute.For<IChatClient>(), this, executor,
                new AgentToolRegistry([new("ai.chat.sessions.metadata", 1, "ai.chat_sessions.read", "read", true, null),
                    new("ai.chat.sessions.rename", 1, "ai.chat_sessions.update", "write", true, null)]), key => AgentWorkflowNodeReceipt.ResolveOperationId(Run, key))
            {
                InputTokenLimit = inputLimit, OutputTokenLimit = 100,
                ModelClientFactory = allocation => { allocations?.Add(allocation); return Substitute.For<IChatClient>(); },
                PersistNodeAsync = persist ?? ((_, _, _) => Task.FromResult(true)),
            };
        }
        public async Task<AgentModelResult> RunAsync(IChatClient client, string prompt, CancellationToken cancellationToken)
        {
            ModelCalls.Enqueue(prompt);
            var role = prompt.Contains("reviewer:", StringComparison.Ordinal);
            var active = Interlocked.Increment(ref Active);
            MaximumActive = Math.Max(MaximumActive, active);
            try
            {
                if (role)
                {
                    IntentBarrierObserved &= State.ParallelPendingNodes.Count == 2;
                    DuringRole?.Invoke();
                    await Task.Delay(20, cancellationToken);
                    if (FailRole) throw new IOException("Fixture branch failed after dispatch.");
                }
                var text = role ? "Role review" : prompt.StartsWith("Propose", StringComparison.Ordinal) ? "Proposed title" : """{"decision":"approved"}""";
                return new(text, JsonSerializer.SerializeToElement(true), 2, 1);
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        public Task ValidateSessionAsync(IChatClient client, JsonElement session, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
