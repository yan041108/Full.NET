using Full.NET.Agents.Definitions;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>静态目录为唯一执行权威；公开绑定只能收紧到精确现有定义，不接受动态 Schema、工具或未评估检索。</summary>
internal static class AiPromptExecutionPolicy
{
    internal static AiPromptExecutionBinding? Resolve(string key)
    {
        if (key == AgentWorkflowRegistry.ChatRenameWorkflowKey)
        {
            var definition = AgentWorkflowRegistry.Resolve(key, AgentWorkflowRegistry.ChatRenameWorkflowVersion)!;
            return new(2, key, definition.Version, AgentWorkflowNodeReceipt.DigestDefinition(definition),
                "ai.workflow.chat_rename.input.v2", "ai.workflow.chat_rename.result.v2",
                ["ai.chat.sessions.list", "ai.chat.sessions.rename"], "none");
        }
        var agent = AgentDefinitionRegistry.Resolve(key, 1);
        if (agent is null) return null;
        var tools = agent.AllowedToolNames.Order(StringComparer.Ordinal).ToArray();
        var digest = AgentWorkflowNodeReceipt.Digest(FormattableString.Invariant(
            $"agent.v2|{agent.Key}|{agent.Version}|{agent.CheckpointFormatVersion}|{agent.MaxToolIterations}|{string.Join('|', tools)}"));
        return new(2, key, agent.Version, digest, "ai.prompt.text.v1", "ai.prompt.text.v1", tools, "none");
    }
    internal static bool Valid(AiPromptBody body)
    {
        var binding = body.Execution;
        if (binding is null) return true;
        if (binding.AllowedToolNames is null || binding.AllowedToolNames.Count > 8) return false;
        var expected = Resolve(binding.DefinitionKey);
        return expected is not null && binding.PolicyVersion == expected.PolicyVersion
            && binding.DefinitionVersion == expected.DefinitionVersion && binding.DefinitionDigest == expected.DefinitionDigest
            && binding.InputSchemaKey == expected.InputSchemaKey && binding.OutputSchemaKey == expected.OutputSchemaKey
            && binding.RetrievalStrategyKey == expected.RetrievalStrategyKey
            && binding.AllowedToolNames.SequenceEqual(expected.AllowedToolNames, StringComparer.Ordinal)
            // 已有重命名工作流只解释注册目录的节点模板，不接受用户改写其静态图。
            && (binding.DefinitionKey != AgentWorkflowRegistry.ChatRenameWorkflowKey || body.Template == "{{input}}");
    }
    internal static string Definition(AiPromptBody body) => body.Execution?.DefinitionKey ?? AgentDefinitionRegistry.SingleTextKey;
}
