using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Agents.Workflows;

/// <summary>冻结工作流与模型版本；静态 Prompt 的修订由定义摘要约束。</summary>
/// <param name="RunId">根运行标识。</param>
/// <param name="WorkflowKey">静态定义键。</param>
/// <param name="WorkflowVersion">定义版本。</param>
/// <param name="DefinitionDigest">节点及静态 Prompt 模板摘要。</param>
/// <param name="ModelConfigId">模型配置标识。</param>
/// <param name="ModelConfigVersion">模型配置版本。</param>
/// <param name="LegacyPrefixCount">旧检查点前缀长度；不为旧节点补造凭证。</param>
public sealed record AgentWorkflowExecutionBinding(Guid RunId, string WorkflowKey, int WorkflowVersion,
    string DefinitionDigest, Guid ModelConfigId, int ModelConfigVersion, int LegacyPrefixCount = 0);

/// <summary>节点意图及完成凭证；输出摘要为空代表派发结果尚未确定，不允许自动重放。</summary>
/// <param name="NodeIndex">定义中的节点序号。</param>
/// <param name="NodeKey">稳定节点键。</param>
/// <param name="OperationId">确定性操作标识。</param>
/// <param name="InputDigest">实际 Prompt 或工具参数的摘要。</param>
/// <param name="OutputDigest">成功输出摘要；意图记录为空。</param>
/// <param name="ToolVersion">工具策略版本；模型节点为空。</param>
/// <param name="InputTokens">本节点输入用量；未知保持空值。</param>
/// <param name="OutputTokens">本节点输出用量；未知保持空值。</param>
/// <param name="InputTokenLimit">派发前冻结的独占输入额度；线性历史为空。</param>
/// <param name="OutputTokenLimit">派发前冻结的独占输出额度；线性历史为空。</param>
public sealed record AgentWorkflowNodeReceipt(int NodeIndex, string NodeKey, Guid OperationId, string InputDigest,
    string? OutputDigest, int? ToolVersion, long? InputTokens, long? OutputTokens,
    long? InputTokenLimit = null, long? OutputTokenLimit = null)
{
    /// <summary>计算文本摘要；摘要不构成外部系统已完成的独立证明。</summary>
    /// <param name="text">待摘要文本。</param>
    /// <returns>大写 SHA-256 十六进制摘要。</returns>
    public static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>按根运行及稳定节点键派生操作标识，恢复不能改变派发身份。</summary>
    /// <param name="runId">根运行标识。</param>
    /// <param name="nodeKey">节点键。</param>
    /// <returns>确定性操作标识。</returns>
    public static Guid ResolveOperationId(Guid runId, string nodeKey) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{runId:N}|workflow|{nodeKey}"))).AsSpan(0, 16), bigEndian: true);

    /// <summary>冻结节点拓扑与全部静态 Prompt，模板改动不能沿用旧节点凭证。</summary>
    /// <param name="definition">静态工作流定义。</param>
    /// <returns>定义摘要。</returns>
    public static string DigestDefinition(AgentWorkflowDefinition definition) => Digest(string.Join('\0',
        definition.Nodes.Select(node => FormattableString.Invariant($"{node.Key}|{node.Kind}|{node.ToolName}|{node.PromptTemplate}")
            + (node.ParallelGroup is null ? string.Empty : $"|parallel:{node.ParallelGroup}"))));

    /// <summary>构造实际输入；执行与恢复校验共用，避免摘要和派发参数偏离。</summary>
    /// <param name="node">当前静态节点。</param>
    /// <param name="state">已完成前缀与关联会话。</param>
    /// <returns>模型 Prompt 文本或工具参数 JSON。</returns>
    public static string BuildInput(AgentWorkflowNode node, AgentWorkflowState state)
    {
        if (node.Kind == AgentWorkflowNodeKind.ModelText)
        {
            var prompt = node.PromptTemplate!;
            foreach (var (key, value) in state.Outputs) prompt = prompt.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
            return prompt;
        }
        if (node.ToolName == "ai.chat.sessions.metadata")
            return JsonSerializer.Serialize(new WorkflowChatSessionArguments(state.TargetChatSessionId ?? Guid.Empty),
                AgentWorkflowJsonContext.Default.WorkflowChatSessionArguments);
        if (node.ToolName == "calendar.personal_schedules.complete")
            return JsonSerializer.Serialize(new WorkflowScheduleCompletionArguments(state.TargetResourceId ?? Guid.Empty,
                state.TargetResourceVersion ?? 0), AgentWorkflowJsonContext.Default.WorkflowScheduleCompletionArguments);
        return node.ToolName == "ai.chat.sessions.rename"
            ? JsonSerializer.Serialize(new WorkflowRenameSessionArguments(state.TargetChatSessionId ?? state.SessionId, state.Outputs.GetValueOrDefault("summarize")?.Trim() ?? string.Empty),
                AgentWorkflowJsonContext.Default.WorkflowRenameSessionArguments)
            : "{}";
    }

    /// <summary>只按静态输入前缀构造真实参数；并行完成顺序不得改变模板替换顺序或摘要。</summary>
    /// <param name="definition">已注册静态定义。</param>
    /// <param name="index">当前节点序号。</param>
    /// <param name="state">持久状态。</param>
    public static string BuildInput(AgentWorkflowDefinition definition, int index, AgentWorkflowState state)
    {
        var prefix = AgentWorkflowState.Create(state.SessionId);
        prefix.TargetChatSessionId = state.TargetChatSessionId;
        prefix.TargetResourceId = state.TargetResourceId;
        prefix.TargetResourceVersion = state.TargetResourceVersion;
        foreach (var node in definition.Nodes.Take(definition.InputPrefixIndex(index)))
            prefix.Outputs.Add(node.Key, state.Outputs.TryGetValue(node.Key, out var value) ? value
                : throw new InvalidDataException("Workflow input prefix is incomplete."));
        return BuildInput(definition.Nodes[index], prefix);
    }
}
