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
        if (state.PendingNode is not null)
            return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, "ai.tool.reconciliation_required");
        if (state.NextNodeIndex < 0 || state.NextNodeIndex > definition.Nodes.Count
            || state.InputTokens is < 0 || state.OutputTokens is < 0)
            return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.checkpoint_incompatible");
        // 恢复到写入或结束位置仍需校验原始结果，不能把节点游标当作校验已通过的证据。
        if (state.NextNodeIndex >= 3 && !HasValidRenameProposal(state))
            return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
        var approvalRequired = false;
        var reconciliationRequired = false;

        for (var index = state.NextNodeIndex; index < definition.Nodes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = definition.Nodes[index];
            AgentWorkflowNodeReceipt? intent = null;
            if (request.PersistNodeAsync is not null)
            {
                intent = new(index, node.Key, request.ResolveOperationId(node.Key),
                    AgentWorkflowNodeReceipt.Digest(AgentWorkflowNodeReceipt.BuildInput(node, state)), null,
                    node.ToolName is null ? null : request.Registry.Find(node.ToolName)?.Version, null, null);
                cancellationToken.ThrowIfCancellationRequested();
                state.PendingNode = intent;
                if (!await request.PersistNodeAsync(intent, state, cancellationToken).ConfigureAwait(false))
                    return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, "ai.tool.reconciliation_required");
                cancellationToken.ThrowIfCancellationRequested();
            }
            long? nodeInputTokens = null, nodeOutputTokens = null;
            var validationFailed = false;
            switch (node.Kind)
            {
                case AgentWorkflowNodeKind.ToolRead:
                case AgentWorkflowNodeKind.ToolWrite:
                {
                    if (node.Kind == AgentWorkflowNodeKind.ToolWrite && !HasValidRenameProposal(state))
                        return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
                    var toolResult = await ExecuteToolNodeAsync(request, node, state, intent?.OperationId, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (toolResult.ApprovalRequired)
                    {
                        // 明确未派发的审批拒绝允许等待态恢复；未知结果仍保留意图。
                        state.PendingNode = null;
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
                    var prompt = AgentWorkflowNodeReceipt.BuildInput(node, state);
                    var modelResult = await request.ModelRunner.RunAsync(request.Client, prompt, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    // 恢复游标前已有模型节点时，空值代表未知累计量；不能把已发生调用当作首次计量。
                    var firstModelCall = !definition.Nodes.Take(index).Any(x => x.Kind == AgentWorkflowNodeKind.ModelText);
                    var inputTokens = AgentTokenUsage.Add(state.InputTokens, modelResult.InputTokens, firstModelCall);
                    var outputTokens = AgentTokenUsage.Add(state.OutputTokens, modelResult.OutputTokens, firstModelCall);
                    // 两类计量均校验成功后一起推进状态，避免第二类溢出留下半份已更新检查点。
                    state.InputTokens = inputTokens;
                    state.OutputTokens = outputTokens;
                    nodeInputTokens = modelResult.InputTokens;
                    nodeOutputTokens = modelResult.OutputTokens;
                    state.Outputs[node.Key] = modelResult.Text;
                    if (string.Equals(node.Key, "validate", StringComparison.Ordinal)
                        && !AgentValidationResult.IsApproved(modelResult.Text))
                    {
                        // 模型已确定返回；先保存实际输出和用量，再拒绝后续业务写入。
                        validationFailed = true;
                    }

                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported workflow node kind '{node.Kind}'.");
            }

            state.NextNodeIndex = index + 1;
            if (intent is not null)
            {
                var receipt = intent with { OutputDigest = AgentWorkflowNodeReceipt.Digest(state.Outputs[node.Key]),
                    InputTokens = nodeInputTokens, OutputTokens = nodeOutputTokens };
                state.NodeReceipts.Add(receipt);
                state.PendingNode = null;
                if (!await request.PersistNodeAsync!(receipt, state, cancellationToken).ConfigureAwait(false))
                    return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, "ai.tool.reconciliation_required");
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (validationFailed)
                return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
        }

        if (approvalRequired)
        {
            return new(AgentWorkflowRunStatus.AwaitingApproval, state, null, null);
        }

        if (reconciliationRequired)
        {
            return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var finalText = state.Outputs.TryGetValue("summarize", out var summary) ? summary : string.Empty;
        return new(AgentWorkflowRunStatus.Completed, state, finalText, null);
    }

    private static async Task<ToolNodeResult> ExecuteToolNodeAsync(
        AgentWorkflowRunRequest request,
        AgentWorkflowNode node,
        AgentWorkflowState state,
        Guid? recordedOperationId,
        CancellationToken cancellationToken)
    {
        var toolName = node.ToolName!;
        var tool = request.Registry.Find(toolName);
        if (tool is null || !tool.IsEnabled)
        {
            return new(false, string.Empty, "ai.tool.unavailable", false, false);
        }

        using var arguments = JsonDocument.Parse(AgentWorkflowNodeReceipt.BuildInput(node, state));
        var operationId = recordedOperationId ?? request.ResolveOperationId(node.Key);
        var invocation = new ToolInvocation(operationId, request.RunId, toolName, tool.Version, arguments.RootElement.Clone());
        cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>工作流执行与检查点准入共享重命名建议校验；结果不代表工具授权或人工审批已通过。</summary>
    /// <param name="state">待校验的工作流状态，不修改其原始标题或校验输出。</param>
    /// <returns>结构化模型校验批准且原始标题符合长度要求时返回true。</returns>
    /// <exception cref="ArgumentNullException">状态为null。</exception>
    public static bool HasValidRenameProposal(AgentWorkflowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // 不截断后再执行，避免实际参数偏离被校验/审批的内容。
        return state.Outputs is not null
            && state.Outputs.TryGetValue("validate", out var validation)
            && AgentValidationResult.IsApproved(validation)
            && state.Outputs.TryGetValue("summarize", out var title)
            && !string.IsNullOrWhiteSpace(title)
            && title.Trim().Length <= 256;
    }

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
    Func<string, Guid> ResolveOperationId)
{
    /// <summary>可选的节点事务提交回调；返回false后停止派发，调用方负责租约与恢复准入。</summary>
    public Func<AgentWorkflowNodeReceipt, AgentWorkflowState, CancellationToken, Task<bool>>? PersistNodeAsync { get; init; }
}
