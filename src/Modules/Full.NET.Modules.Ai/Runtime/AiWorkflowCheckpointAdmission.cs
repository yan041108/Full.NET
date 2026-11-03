using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>创建模型客户端前校验检查点与节点凭证；未确定的派发意图由协调器转入对账。</summary>
internal static class AiWorkflowCheckpointAdmission
{
    /// <summary>无检查点返回初始状态；不兼容、损坏或缺失已完成节点输出时返回null，不自动修补或重放。</summary>
    internal static AgentWorkflowState? Restore(AiAgentRunRecord run, AgentCheckpointRecord? checkpoint)
    {
        var definition = AgentWorkflowRegistry.Resolve(run.DefinitionKey, run.DefinitionVersion);
        if (definition is null) return null;
        if (checkpoint is null) return AgentWorkflowState.Create(run.SessionId);
        if (checkpoint.RunId != run.Id || checkpoint.CheckpointId == Guid.Empty
            || checkpoint.Sequence <= 0 || checkpoint.Sequence == long.MaxValue
            || checkpoint.DefinitionVersion != run.DefinitionVersion
            || string.IsNullOrWhiteSpace(checkpoint.PayloadProtected)
            || !AgentCheckpointCompatibility.TryValidateWorkflow(run.DefinitionKey, checkpoint.DefinitionVersion,
                checkpoint.FormatVersion, checkpoint.FrameworkVersion, out _)) return null;

        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checkpoint.PayloadProtected)));
        if (!string.Equals(checksum, checkpoint.Checksum, StringComparison.OrdinalIgnoreCase)) return null;
        AgentWorkflowState? state;
        try { state = AgentWorkflowState.FromJson(checkpoint.PayloadProtected); }
        catch (JsonException) { return null; }
        if (state is null || state.SessionId != run.SessionId || state.Outputs is null || state.NodeReceipts is null || state.ParallelPendingNodes is null
            || state.NextNodeIndex < 0 || state.NextNodeIndex > definition.Nodes.Count
            || state.InputTokens is < 0 || state.OutputTokens is < 0
            || state.Outputs.Count > definition.Nodes.Count || state.NodeReceipts.Count > definition.Nodes.Count
            || state.ParallelPendingNodes.Count > 2
            || state.Outputs.Any(entry => entry.Value is null || entry.Value.Length > 65536)
            || state.PendingNode is not null && state.ParallelPendingNodes.Count != 0) return null;
        // 游标前每个节点必须留有输出；缺失不等于尚未执行，不能携带占位模板重新派发。
        if (definition.Nodes.Take(state.NextNodeIndex).Any(node =>
                !state.Outputs.TryGetValue(node.Key, out var output) || string.IsNullOrWhiteSpace(output))) return null;
        if (definition.Nodes.Take(state.NextNodeIndex).Any(node => node.Key == "validate") && !AgentWorkflowRunner.HasValidRenameProposal(state)) return null;
        if (state.Binding is null)
            return state.PendingNode is null && state.NodeReceipts.Count == 0 && state.ParallelPendingNodes.Count == 0
                && !definition.Nodes.Take(state.NextNodeIndex).Any(node => node.ParallelGroup is not null) ? state : null;
        var binding = state.Binding;
        if (binding.RunId != run.Id || binding.WorkflowKey != run.DefinitionKey || binding.WorkflowVersion != run.DefinitionVersion
            || binding.DefinitionDigest != AgentWorkflowNodeReceipt.DigestDefinition(definition)
            || binding.ModelConfigId == Guid.Empty || binding.ModelConfigVersion <= 0
            || binding.LegacyPrefixCount < 0 || binding.LegacyPrefixCount > state.NextNodeIndex
            || binding.LegacyPrefixCount != 0 && definition.Nodes.Any(node => node.ParallelGroup is not null)) return null;
        var indices = new HashSet<int>();
        var operations = new HashSet<Guid>();
        var outputs = definition.Nodes.Take(binding.LegacyPrefixCount).Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var receipt in state.NodeReceipts)
        {
            if (receipt is null || receipt.NodeIndex < binding.LegacyPrefixCount || receipt.NodeIndex >= definition.Nodes.Count
                || !indices.Add(receipt.NodeIndex) || !operations.Add(receipt.OperationId)
                || !state.Outputs.TryGetValue(receipt.NodeKey, out var output) || string.IsNullOrWhiteSpace(output)
                || !ValidReceipt(receipt, receipt.NodeIndex, run.Id, state, definition)
                || receipt.OutputDigest != AgentWorkflowNodeReceipt.Digest(output)) return null;
            outputs.Add(receipt.NodeKey);
        }
        var cursor = binding.LegacyPrefixCount;
        while (cursor < definition.Nodes.Count && indices.Contains(cursor)) cursor++;
        if (cursor != state.NextNodeIndex || !outputs.SetEquals(state.Outputs.Keys)) return null;
        var waveStart = cursor;
        var waveEnd = cursor;
        if (cursor < definition.Nodes.Count && definition.Nodes[cursor].ParallelGroup is not null)
        {
            var start = definition.InputPrefixIndex(cursor);
            waveStart = start + ((cursor - start) / 2 * 2);
            waveEnd = Math.Min(waveStart + 2, definition.GroupEndIndex(cursor));
        }
        // 游标后的完成项只能属于当前并行波次，不能预置下个阶段的输出跳过派发。
        if (indices.Any(index => index >= cursor && (index < waveStart || index >= waveEnd))) return null;
        var pendingIndices = new HashSet<int>();
        foreach (var pending in state.ParallelPendingNodes)
        {
            if (pending is null || pending.NodeIndex < waveStart || pending.NodeIndex >= waveEnd
                || indices.Contains(pending.NodeIndex) || !pendingIndices.Add(pending.NodeIndex) || !operations.Add(pending.OperationId)
                || pending.OutputDigest is not null || pending.InputTokens is not null || pending.OutputTokens is not null
                || !ValidReceipt(pending, pending.NodeIndex, run.Id, state, definition)) return null;
        }
        // 两个意图均保存后才会派发；存在已完成分支却缺少同波伙伴意图，不得推断伙伴未调用。
        if (waveEnd > waveStart && Enumerable.Range(waveStart, waveEnd - waveStart).Any(indices.Contains)
            && Enumerable.Range(waveStart, waveEnd - waveStart).Any(index => !indices.Contains(index) && !pendingIndices.Contains(index))) return null;
        if (state.PendingNode is { } linearPending
            && (state.NextNodeIndex >= definition.Nodes.Count || definition.Nodes[state.NextNodeIndex].ParallelGroup is not null || linearPending.OutputDigest is not null
                || linearPending.InputTokens is not null || linearPending.OutputTokens is not null
                || !operations.Add(linearPending.OperationId)
                || !ValidReceipt(linearPending, state.NextNodeIndex, run.Id, state, definition))) return null;
        // 结构可信但结果未确定的派发必须先对账，计数损坏不能触发结算或释放原预留。
        if (state.PendingNode is null && pendingIndices.Count == 0
            && state.ExecutionSteps is { } steps && steps < binding.LegacyPrefixCount + indices.Count) return null;
        return state;
    }

    private static bool ValidReceipt(AgentWorkflowNodeReceipt? receipt, int index, Guid runId,
        AgentWorkflowState state, AgentWorkflowDefinition definition)
    {
        if (receipt is null || index < 0 || index >= definition.Nodes.Count) return false;
        var node = definition.Nodes[index];
        // 只用节点前缀重建原输入，后续输出不得改写先前节点的摘要。
        var prefix = AgentWorkflowState.Create(state.SessionId);
        prefix.TargetChatSessionId = state.TargetChatSessionId;
        prefix.TargetResourceId = state.TargetResourceId;
        prefix.TargetResourceVersion = state.TargetResourceVersion;
        foreach (var prior in definition.Nodes.Take(definition.InputPrefixIndex(index)))
        {
            if (!state.Outputs.TryGetValue(prior.Key, out var output)) return false;
            prefix.Outputs.Add(prior.Key, output);
        }
        return receipt.NodeIndex == index && receipt.NodeKey == node.Key
            && receipt.OperationId == AgentWorkflowNodeReceipt.ResolveOperationId(runId, node.Key)
            && receipt.InputDigest == AgentWorkflowNodeReceipt.Digest(AgentWorkflowNodeReceipt.BuildInput(node, prefix))
            && receipt.InputTokens is not < 0 && receipt.OutputTokens is not < 0
            && (receipt.InputTokenLimit is null && receipt.OutputTokenLimit is null && node.ParallelGroup is null
                || receipt.InputTokenLimit is > 0 and <= 1000000000 && receipt.OutputTokenLimit is > 0 and <= 1000000000
                && (receipt.InputTokens is null || receipt.InputTokens <= receipt.InputTokenLimit)
                && (receipt.OutputTokens is null || receipt.OutputTokens <= receipt.OutputTokenLimit))
            && (node.Kind == AgentWorkflowNodeKind.ModelText ? receipt.ToolVersion is null
                : receipt.ToolVersion is > 0 && receipt.InputTokens is null && receipt.OutputTokens is null);
    }
}
