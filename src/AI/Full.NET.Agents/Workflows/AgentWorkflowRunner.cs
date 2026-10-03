using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Workflows;

/// <summary>显式工作流执行器；节点顺序静态，子步骤共用根 RunId 与独立 OperationId。</summary>
public sealed class AgentWorkflowRunner
{
    /// <summary>
    /// 从请求中的 NextNodeIndex 开始顺序执行工作流节点，直至完成、失败或需要人工审批/对账。
    /// </summary>
    /// <remarks>
    /// 该方法非线程安全；同一 <see cref="AgentWorkflowRunRequest.State"/> 不可被并发调用。
    /// 遇到 ToolRead/ToolWrite 节点返回审批或对账要求时，会把 NextNodeIndex 回退到当前节点并立即返回，
    /// 以便调用方在外部完成审批后重入。
    /// </remarks>
    /// <param name="request">工作流执行请求；包含 RunId、状态、模型与工具执行器。</param>
    /// <param name="cancellationToken">用于取消执行的令牌；节点之间会检查取消。</param>
    /// <returns>执行结果，包含状态、更新后的状态、最终摘要文本与错误码。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">工作流定义不存在或遇到不支持的节点类型。</exception>
    public async Task<AgentWorkflowRunResult> RunAsync(AgentWorkflowRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = AgentWorkflowRegistry.Resolve(request.WorkflowKey, request.WorkflowVersion);
        if (definition is null)
            return new(AgentWorkflowRunStatus.Failed, request.State, null, "ai.agent_run.definition_incompatible");

        var state = request.State;
        if (state.NextNodeIndex < 0 || state.NextNodeIndex > definition.Nodes.Count)
            return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.checkpoint_incompatible");
        // 恢复到写入或结束位置仍需校验原始结果，不能把节点游标当作校验已通过的证据。
        if (state.NextNodeIndex >= 3 && !CanRename(state))
            return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
        var approvalRequired = false;
        var reconciliationRequired = false;

        for (var index = state.NextNodeIndex; index < definition.Nodes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = definition.Nodes[index];
            switch (node.Kind)
            {
                case AgentWorkflowNodeKind.ToolRead:
                case AgentWorkflowNodeKind.ToolWrite:
                {
                    if (node.Kind == AgentWorkflowNodeKind.ToolWrite && !CanRename(state))
                        return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
                    var toolResult = await ExecuteToolNodeAsync(request, node, state, cancellationToken).ConfigureAwait(false);
                    if (toolResult.ApprovalRequired)
                    {
                        approvalRequired = true;
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.AwaitingApproval, state, null, null);
                    }

                    if (toolResult.ReconciliationRequired)
                    {
                        reconciliationRequired = true;
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, null);
                    }

                    if (!toolResult.Succeeded)
                    {
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.Failed, state, null, toolResult.ErrorCode ?? "ai.agent_run.workflow_tool_failed");
                    }

                    state.Outputs[node.Key] = toolResult.Output;
                    break;
                }
                case AgentWorkflowNodeKind.ModelText:
                {
                    var prompt = Interpolate(node.PromptTemplate!, state.Outputs);
                    var modelResult = await request.ModelRunner.RunAsync(request.Client, prompt, cancellationToken).ConfigureAwait(false);
                    state.InputTokens = AddUsage(state.InputTokens, modelResult.InputTokens);
                    state.OutputTokens = AddUsage(state.OutputTokens, modelResult.OutputTokens);
                    state.Outputs[node.Key] = modelResult.Text;
                    if (string.Equals(node.Key, "validate", StringComparison.Ordinal)
                        && !AgentValidationResult.IsApproved(modelResult.Text))
                    {
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
                    }

                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported workflow node kind '{node.Kind}'.");
            }

            state.NextNodeIndex = index + 1;
        }

        if (approvalRequired)
        {
            return new(AgentWorkflowRunStatus.AwaitingApproval, state, null, null);
        }

        if (reconciliationRequired)
        {
            return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, null);
        }

        var finalText = state.Outputs.TryGetValue("summarize", out var summary) ? summary : string.Empty;
        return new(AgentWorkflowRunStatus.Completed, state, finalText, null);
    }

    private static async Task<ToolNodeResult> ExecuteToolNodeAsync(
        AgentWorkflowRunRequest request,
        AgentWorkflowNode node,
        AgentWorkflowState state,
        CancellationToken cancellationToken)
    {
        var toolName = node.ToolName!;
        var tool = request.Registry.Find(toolName);
        if (tool is null || !tool.IsEnabled)
        {
            return new(false, string.Empty, "ai.tool.unavailable", false, false);
        }

        using var arguments = BuildToolArguments(node, state);
        var operationId = request.ResolveOperationId(node.Key);
        var invocation = new ToolInvocation(operationId, request.RunId, toolName, tool.Version, arguments.RootElement.Clone());
        var result = await request.Executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (string.Equals(result.ErrorCode, "ai.tool.approval_required", StringComparison.Ordinal))
        {
            return new(false, string.Empty, result.ErrorCode, true, false);
        }

        if (string.Equals(result.ErrorCode, "ai.tool.reconciliation_required", StringComparison.Ordinal))
        {
            return new(false, string.Empty, result.ErrorCode, false, true);
        }

        if (!string.Equals(result.StatusKey, "succeeded", StringComparison.Ordinal))
        {
            return new(false, string.Empty, result.ErrorCode ?? "ai.tool.failed", false, false);
        }

        var output = result.Value?.GetRawText() ?? "null";
        return new(true, output, null, false, false);
    }

    private static JsonDocument BuildToolArguments(AgentWorkflowNode node, AgentWorkflowState state)
    {
        if (string.Equals(node.ToolName, "ai.chat.sessions.rename", StringComparison.Ordinal))
        {
            var title = state.Outputs.TryGetValue("summarize", out var proposed) ? proposed.Trim() : string.Empty;

            return JsonDocument.Parse(JsonSerializer.Serialize(
                new WorkflowRenameSessionArguments(state.SessionId, title),
                AgentWorkflowJsonContext.Default.WorkflowRenameSessionArguments));
        }

        return JsonDocument.Parse("{}");
    }

    // 标题规则由程序独立保证；不截断后再执行，避免实际参数偏离被校验/审批的内容。
    private static bool CanRename(AgentWorkflowState state) =>
        state.Outputs.TryGetValue("validate", out var validation)
        && AgentValidationResult.IsApproved(validation)
        && state.Outputs.TryGetValue("summarize", out var title)
        && !string.IsNullOrWhiteSpace(title)
        && title.Trim().Length <= 256;

    private static string Interpolate(string template, IReadOnlyDictionary<string, string> outputs)
    {
        var result = template;
        foreach (var (key, value) in outputs)
        {
            result = result.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        return result;
    }

    private static long? AddUsage(long? current, long? delta) =>
        current is null && delta is null ? null : (current ?? 0) + (delta ?? 0);

    private sealed record ToolNodeResult(
        bool Succeeded,
        string Output,
        string? ErrorCode,
        bool ApprovalRequired,
        bool ReconciliationRequired);
}

/// <summary>工作流执行请求；OperationId 由调用方按节点键派生。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RunId">本次工作流运行的根标识；所有子步骤共用，用于跨节点追踪。</param>
/// <param name="WorkflowKey">稳定工作流定义键；发布后不可改名。</param>
/// <param name="WorkflowVersion">工作流定义版本；从 1 开始单调递增。</param>
/// <param name="State">可变工作流状态；包含 NextNodeIndex、Outputs 与 token 用量。</param>
/// <param name="Client">模型调用所用的 IChatClient；由调用方负责生命周期。</param>
/// <param name="ModelRunner">模型文本节点执行器。</param>
/// <param name="Executor">工具节点执行器。</param>
/// <param name="Registry">工具注册表，用于按 ToolName 查找并校验工具可用性。</param>
/// <param name="ResolveOperationId">按节点键派生 OperationId 的工厂；保证每次调用幂等。</param>
public sealed record AgentWorkflowRunRequest(
    Guid RunId,
    string WorkflowKey,
    int WorkflowVersion,
    AgentWorkflowState State,
    IChatClient Client,
    IAgentModelRunner ModelRunner,
    IAgentToolExecutor Executor,
    AgentToolRegistry Registry,
    Func<string, Guid> ResolveOperationId);
