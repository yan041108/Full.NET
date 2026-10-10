using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>创建模型客户端前校验工作流检查点；完整性摘要不能替代节点持久完成凭证。</summary>
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
        return state;
    }
}
