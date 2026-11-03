using System.Collections.Concurrent;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Microsoft.Extensions.AI;

// 原生探针只验证真实框架适配器、并行状态序列化与恢复；不伪造 API、数据库或云模型验收。
await VerifyAsync(false);
await VerifyAsync(true);
Console.WriteLine("Native parallel workflow probe: 2 scenarios passed.");

static async Task VerifyAsync(bool restart)
{
    var run = Guid.CreateVersion7(); var target = Guid.CreateVersion7();
    var state = AgentWorkflowState.Create(Guid.CreateVersion7()); state.TargetChatSessionId = target;
    var definition = AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatReviewWorkflowKey, 1)!;
    state.Binding = new(run, definition.Key, 1, AgentWorkflowNodeReceipt.DigestDefinition(definition), Guid.CreateVersion7(), 1);
    using var client = new ProbeClient(); var tools = new ProbeTools(target);
    var registry = new AgentToolRegistry([new("ai.chat.sessions.metadata", 1, "ai.chat.sessions.read", "read", true, null),
        new("ai.chat.sessions.rename", 1, "ai.chat.sessions.update", "write", true, null)]);
    var runner = new AgentWorkflowRunner(); string? saved = null;
    var allocations = new List<long>();
    AgentWorkflowRunRequest Request(bool pause) => new(run, definition.Key, 1, state, client, AgentFrameworkRuntime.CreateRunner(), tools, registry,
        key => AgentWorkflowNodeReceipt.ResolveOperationId(run, key))
    {
        InputTokenLimit = 100, OutputTokenLimit = 100,
        ModelClientFactory = budget => { allocations.Add(budget.InputTokenLimit); return new BorrowedClient(client); },
        PersistNodeAsync = (_, current, _) =>
        {
            // 每次真实派发前、完成后均往返源生成 JSON，包含并行意图和配额。
            saved = current.ToJson();
            Require(AgentWorkflowState.FromJson(saved)!.ParallelPendingNodes.Count == current.ParallelPendingNodes.Count, "pending JSON");
            return Task.FromResult(!(pause && current.NextNodeIndex == 3 && current.ParallelPendingNodes.Count == 0));
        }
    };
    var result = await runner.RunAsync(Request(restart));
    if (restart)
    {
        Require(result.Status == AgentWorkflowRunStatus.ReconciliationRequired && client.Calls.Count == 2, "wave handoff");
        state = AgentWorkflowState.FromJson(saved!)!;
        result = await runner.RunAsync(Request(false));
    }
    Require(result.Status == AgentWorkflowRunStatus.AwaitingApproval && client.Calls.Count == 6 && client.Maximum == 2, "join/approval");
    Require(state.InputTokens == 12 && state.OutputTokens == 6 && state.ParallelPendingNodes.Count == 0, "parent usage");
    Require(allocations.SequenceEqual([50L, 50L, 48L, 48L, 92L, 90L]), "allocation");
    Require(tools.Writes == 0 && state.TargetChatSessionId == target && target != state.SessionId, "target identity");
    state = AgentWorkflowState.FromJson(state.ToJson())!;
    tools.Approved = true;
    result = await runner.RunAsync(Request(false));
    Require(result.Status == AgentWorkflowRunStatus.Completed && tools.Writes == 1 && client.Calls.Count == 6 && client.Active == 0, "resume once");
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class ProbeTools(Guid target) : IAgentToolExecutor
{
    internal bool Approved; internal int Writes;
    public ValueTask<ToolExecutionResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Arguments.GetProperty("sessionId").GetGuid() != target) throw new InvalidOperationException("Wrong target.");
        if (invocation.ToolName == "ai.chat.sessions.metadata")
        { using var json = JsonDocument.Parse("{\"title\":\"Original\",\"version\":\"1\"}"); return ValueTask.FromResult(new ToolExecutionResult("succeeded", json.RootElement.Clone(), null)); }
        if (!Approved) return ValueTask.FromResult(new ToolExecutionResult("denied", null, "ai.tool.approval_required"));
        if (invocation.Arguments.GetProperty("title").GetString() != "Reviewed title") throw new InvalidOperationException("Wrong title.");
        Writes++; using var success = JsonDocument.Parse("true"); return ValueTask.FromResult(new ToolExecutionResult("succeeded", success.RootElement.Clone(), null));
    }
}
sealed class ProbeClient : IChatClient
{
    internal readonly ConcurrentQueue<string> Calls = new();
    internal int Active, Maximum;
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", messages.Select(message => message.Text)); Calls.Enqueue(prompt);
        var active = Interlocked.Increment(ref Active);
        int observed;
        do { observed = Maximum; } while (active > observed && Interlocked.CompareExchange(ref Maximum, active, observed) != observed);
        try
        {
            var role = prompt.Contains("reviewer:", StringComparison.Ordinal);
            if (role) await Task.Delay(30, cancellationToken);
            var text = role ? "Review" : prompt.StartsWith("Propose", StringComparison.Ordinal) ? "Reviewed title" : """{"decision":"approved"}""";
            return new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new() { InputTokenCount = 2, OutputTokenCount = 1 } };
        }
        finally { Interlocked.Decrement(ref Active); }
    }
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(IChatClient) && serviceKey is null ? this : null;
    public void Dispose() { }
}
// 分支仅借用底层客户端，根作用域负责等待全部分支后释放网络资源。
sealed class BorrowedClient(ProbeClient inner) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => inner.GetResponseAsync(messages, options, cancellationToken);
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => inner.GetStreamingResponseAsync(messages, options, cancellationToken);
    public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);
    public void Dispose() { }
}
