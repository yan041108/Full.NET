using Full.NET.Agents.Definitions;
using Full.NET.Agents.Workflows;

namespace Full.NET.Agents.Runtime;

/// <summary>恢复前校验检查点版本；不兼容版本失败关闭，不自动迁移旧状态。</summary>
public static class AgentCheckpointCompatibility
{
    public const int CurrentCheckpointFormatVersion = 1;

    public static bool TryValidate(
        string definitionKey,
        int definitionVersion,
        int checkpointFormatVersion,
        string frameworkVersion,
        out string errorCode) =>
        TryValidateDefinition(definitionKey, definitionVersion, checkpointFormatVersion, frameworkVersion, out errorCode);

    public static bool TryValidateWorkflow(
        string workflowKey,
        int workflowVersion,
        int checkpointFormatVersion,
        string frameworkVersion,
        out string errorCode) =>
        TryValidateDefinition(workflowKey, workflowVersion, checkpointFormatVersion, frameworkVersion, out errorCode);

    private static bool TryValidateDefinition(
        string definitionKey,
        int definitionVersion,
        int checkpointFormatVersion,
        string frameworkVersion,
        out string errorCode)
    {
        errorCode = string.Empty;
        if (AgentDefinitionRegistry.Resolve(definitionKey, definitionVersion) is null
            && AgentWorkflowRegistry.Resolve(definitionKey, definitionVersion) is null)
        {
            errorCode = "ai.agent_run.definition_incompatible";
            return false;
        }

        if (checkpointFormatVersion != CurrentCheckpointFormatVersion)
        {
            errorCode = "ai.agent_run.checkpoint_format_incompatible";
            return false;
        }

        if (!string.Equals(frameworkVersion, AgentFrameworkRuntime.FrameworkVersion, StringComparison.Ordinal))
        {
            errorCode = "ai.agent_run.framework_incompatible";
            return false;
        }

        return true;
    }
}
