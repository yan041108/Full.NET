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
        if (state is null || state.SessionId != run.SessionId || state.Outputs is null
            || state.NextNodeIndex < 0 || state.NextNodeIndex > definition.Nodes.Count
            || state.InputTokens is < 0 || state.OutputTokens is < 0
            || state.Outputs.Any(entry => entry.Value is null)) return null;
        // 游标前每个节点必须留有输出；缺失不等于尚未执行，不能携带占位模板重新派发。
        if (definition.Nodes.Take(state.NextNodeIndex).Any(node =>
                !state.Outputs.TryGetValue(node.Key, out var output) || string.IsNullOrWhiteSpace(output))) return null;
        if (state.NextNodeIndex >= 3 && !AgentWorkflowRunner.HasValidRenameProposal(state)) return null;
        if (state.NodeReceipts is null) return null;
        if (state.Binding is null)
            return state.PendingNode is null && state.NodeReceipts.Count == 0 ? state : null;
        var binding = state.Binding;
        if (binding.RunId != run.Id || binding.WorkflowKey != run.DefinitionKey || binding.WorkflowVersion != run.DefinitionVersion
            || binding.DefinitionDigest != AgentWorkflowNodeReceipt.DigestDefinition(definition)
            || binding.ModelConfigId == Guid.Empty || binding.ModelConfigVersion <= 0
            || binding.LegacyPrefixCount < 0 || binding.LegacyPrefixCount > state.NextNodeIndex
            || state.NodeReceipts.Count != state.NextNodeIndex - binding.LegacyPrefixCount) return null;
        for (var index = binding.LegacyPrefixCount; index < state.NextNodeIndex; index++)
        {
            var receipt = state.NodeReceipts[index - binding.LegacyPrefixCount];
            if (!ValidReceipt(receipt, index, run.Id, state, definition)
                || receipt.OutputDigest != AgentWorkflowNodeReceipt.Digest(state.Outputs[definition.Nodes[index].Key])) return null;
        }
        if (state.PendingNode is { } pending
            && (state.NextNodeIndex >= definition.Nodes.Count || pending.OutputDigest is not null
                || pending.InputTokens is not null || pending.OutputTokens is not null
                || !ValidReceipt(pending, state.NextNodeIndex, run.Id, state, definition))) return null;
        return state;
    }

    private static bool ValidReceipt(AgentWorkflowNodeReceipt? receipt, int index, Guid runId,
        AgentWorkflowState state, AgentWorkflowDefinition definition)
    {
        if (receipt is null) return false;
        var node = definition.Nodes[index];
        // 只用节点前缀重建原输入，后续输出不得改写先前节点的摘要。
        var prefix = AgentWorkflowState.Create(state.SessionId);
        foreach (var prior in definition.Nodes.Take(index)) prefix.Outputs.Add(prior.Key, state.Outputs[prior.Key]);
        return receipt.NodeIndex == index && receipt.NodeKey == node.Key
            && receipt.OperationId == AgentWorkflowNodeReceipt.ResolveOperationId(runId, node.Key)
            && receipt.InputDigest == AgentWorkflowNodeReceipt.Digest(AgentWorkflowNodeReceipt.BuildInput(node, prefix))
            && receipt.InputTokens is not < 0 && receipt.OutputTokens is not < 0
            && (node.Kind == AgentWorkflowNodeKind.ModelText ? receipt.ToolVersion is null
                : receipt.ToolVersion is > 0 && receipt.InputTokens is null && receipt.OutputTokens is null);
    }
}
