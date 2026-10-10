using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Workflows;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>创建模型客户端前核对冻结预算与已完成用量；未知历史不授予新的模型额度。</summary>
internal static class AiRunOutputBudgetAdmission
{
    internal static bool TryGetConsumedTokens(AgentRunBudgetSnapshot snapshot, AgentWorkflowState? state, out long? consumedOutputTokens)
        => TryGetConsumedTokens(snapshot, state, out _, out consumedOutputTokens);

    internal static bool TryGetConsumedTokens(AgentRunBudgetSnapshot snapshot, AgentWorkflowState? state,
        out long? consumedInputTokens, out long? consumedOutputTokens)
    {
        consumedInputTokens = 0;
        consumedOutputTokens = 0;
        if (snapshot.InputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens
            || snapshot.OutputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens) return false;
        if (state is null) return true;
        if (state.InputTokens is < 0 || state.InputTokens > snapshot.InputTokenLimit
            || state.OutputTokens is < 0 || state.OutputTokens > snapshot.OutputTokenLimit) return false;
        var definition = AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion)!;
        // 检查点结构已由恢复准入验证；再以模型节点凭证核对合计，防止低报或把未知改成零。
        if (state.Binding is { } binding)
        {
            var legacyModel = definition.Nodes.Take(binding.LegacyPrefixCount).Any(node => node.Kind == AgentWorkflowNodeKind.ModelText);
            var receipts = state.NodeReceipts.Where(receipt => definition.Nodes[receipt.NodeIndex].Kind == AgentWorkflowNodeKind.ModelText).ToArray();
            if (!MatchesUsage(state.InputTokens, receipts.Select(receipt => receipt.InputTokens), legacyModel)
                || !MatchesUsage(state.OutputTokens, receipts.Select(receipt => receipt.OutputTokens), legacyModel)) return false;
        }
        var hadModel = definition.Nodes.Take(state.NextNodeIndex).Any(node => node.Kind == AgentWorkflowNodeKind.ModelText);
        var needsModel = definition.Nodes.Skip(state.NextNodeIndex).Any(node => node.Kind == AgentWorkflowNodeKind.ModelText);
        consumedInputTokens = hadModel ? state.InputTokens : 0;
        consumedOutputTokens = hadModel ? state.OutputTokens : 0;
        // 已完成运行可以继续保存既有事实与未知结算；有剩余模型时，空值或耗尽不得当作首次调用。
        return !needsModel || consumedOutputTokens is { } used && used < snapshot.OutputTokenLimit
            && consumedInputTokens is { } input && input < snapshot.InputTokenLimit;
    }

    private static bool MatchesUsage(long? total, IEnumerable<long?> receipts, bool legacyModel)
    {
        long sum = 0;
        var unknown = false;
        var hasModel = false;
        foreach (var value in receipts)
        {
            hasModel = true;
            if (!value.HasValue) { unknown = true; continue; }
            if (value < 0 || value > long.MaxValue - sum) return false;
            sum += value.Value;
        }
        if (unknown) return total is null;
        // 旧前缀没有节点计量证明，只核对新凭证的下限，不补造旧用量。
        if (legacyModel) return total is null || total >= sum;
        return total == sum || !hasModel && total is null;
    }
}
