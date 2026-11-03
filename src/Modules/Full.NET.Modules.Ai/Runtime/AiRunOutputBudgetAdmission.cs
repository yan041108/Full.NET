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
        var definition = state.Binding is { } stored
            ? AgentWorkflowRegistry.Resolve(stored.WorkflowKey, stored.WorkflowVersion)
            : AgentWorkflowRegistry.Resolve(AgentWorkflowRegistry.ChatRenameWorkflowKey, AgentWorkflowRegistry.ChatRenameWorkflowVersion);
        if (definition is null || state.NodeReceipts is null || state.ParallelPendingNodes is null
            || state.NodeReceipts.Any(receipt => receipt.NodeIndex < 0 || receipt.NodeIndex >= definition.Nodes.Count)) return false;
        // 检查点结构已由恢复准入验证；再以模型节点凭证核对合计，防止低报或把未知改成零。
        if (state.Binding is { } binding)
        {
            var legacyModel = definition.Nodes.Take(binding.LegacyPrefixCount).Any(node => node.Kind == AgentWorkflowNodeKind.ModelText);
            var receipts = state.NodeReceipts.Where(receipt => definition.Nodes[receipt.NodeIndex].Kind == AgentWorkflowNodeKind.ModelText).ToArray();
            if (!MatchesUsage(state.InputTokens, receipts.Select(receipt => receipt.InputTokens), legacyModel)
                || !MatchesUsage(state.OutputTokens, receipts.Select(receipt => receipt.OutputTokens), legacyModel)) return false;
            if (!ValidAllocations(snapshot, state, definition)) return false;
        }
        var hadModel = definition.Nodes.Take(state.NextNodeIndex).Any(node => node.Kind == AgentWorkflowNodeKind.ModelText)
            || state.NodeReceipts.Any(receipt => receipt.ToolVersion is null);
        var needsModel = definition.Nodes.Select((node, index) => (node, index))
            .Any(item => item.node.Kind == AgentWorkflowNodeKind.ModelText && item.index >= state.NextNodeIndex
                && !state.NodeReceipts.Any(receipt => receipt.NodeIndex == item.index));
        consumedInputTokens = hadModel ? state.InputTokens : 0;
        consumedOutputTokens = hadModel ? state.OutputTokens : 0;
        // 已完成运行可以继续保存既有事实与未知结算；有剩余模型时，空值或耗尽不得当作首次调用。
        return !needsModel || consumedOutputTokens is { } used && used < snapshot.OutputTokenLimit
            && consumedInputTokens is { } input && input < snapshot.InputTokenLimit;
    }

    private static bool ValidAllocations(AgentRunBudgetSnapshot budget, AgentWorkflowState state, AgentWorkflowDefinition definition)
    {
        var all = state.NodeReceipts.Concat(state.ParallelPendingNodes).Concat(state.PendingNode is { } pending ? [pending] : []).ToArray();
        var parallel = definition.Nodes.Any(node => node.ParallelGroup is not null);
        foreach (var receipt in all)
        {
            if (receipt.NodeIndex < 0 || receipt.NodeIndex >= definition.Nodes.Count) return false;
            var node = definition.Nodes[receipt.NodeIndex];
            if (node.Kind != AgentWorkflowNodeKind.ModelText) continue;
            if (receipt.InputTokenLimit is null && receipt.OutputTokenLimit is null) { if (parallel) return false; continue; }
            var start = definition.InputPrefixIndex(receipt.NodeIndex);
            var waveStart = node.ParallelGroup is null ? receipt.NodeIndex : start + ((receipt.NodeIndex - start) / 2 * 2);
            var slots = node.ParallelGroup is null ? 1 : Math.Min(2, definition.GroupEndIndex(receipt.NodeIndex) - waveStart);
            long input = 0, output = 0;
            foreach (var prior in state.NodeReceipts.Where(item => item.NodeIndex < waveStart && item.ToolVersion is null))
            {
                if (prior.InputTokens is null or < 0 || prior.OutputTokens is null or < 0
                    || input > budget.InputTokenLimit - prior.InputTokens.Value || output > budget.OutputTokenLimit - prior.OutputTokens.Value) return false;
                input += prior.InputTokens.Value; output += prior.OutputTokens.Value;
            }
            if (receipt.InputTokenLimit != (budget.InputTokenLimit - input) / slots
                || receipt.OutputTokenLimit != (budget.OutputTokenLimit - output) / slots
                || receipt.InputTokenLimit is not > 0 || receipt.OutputTokenLimit is not > 0) return false;
        }
        return true;
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
