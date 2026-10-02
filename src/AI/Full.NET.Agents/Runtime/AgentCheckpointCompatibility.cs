using Full.NET.Agents.Definitions;
using Full.NET.Agents.Workflows;

namespace Full.NET.Agents.Runtime;

/// <summary>恢复前校验检查点版本；不兼容版本失败关闭，不自动迁移旧状态。</summary>
/// <remarks>
/// 常量字符串发布后不可改名或删除；新增常量只能追加。
/// </remarks>
public static class AgentCheckpointCompatibility
{
    /// <summary>当前 Checkpoint 序列化格式版本号；恢复时必须与此值相等，否则按不兼容处理并失败关闭。</summary>
    public const int CurrentCheckpointFormatVersion = 1;

    /// <summary>校验 Agent Definition 与 Checkpoint 的兼容性：定义必须已注册、Checkpoint 格式版本必须等于当前版本、框架版本必须精确匹配。</summary>
    /// <param name="definitionKey">Agent 定义稳定键。</param>
    /// <param name="definitionVersion">Agent 定义版本号。</param>
    /// <param name="checkpointFormatVersion">Checkpoint 序列化格式版本号。</param>
    /// <param name="frameworkVersion">生成 Checkpoint 的 Agent 框架版本。</param>
    /// <param name="errorCode">校验失败时输出的稳定错误码；成功时为空字符串。</param>
    /// <returns>全部兼容返回 true；否则返回 false 并通过 errorCode 暴露失败原因。</returns>
    public static bool TryValidate(
        string definitionKey,
        int definitionVersion,
        int checkpointFormatVersion,
        string frameworkVersion,
        out string errorCode) =>
        TryValidateDefinition(definitionKey, definitionVersion, checkpointFormatVersion, frameworkVersion, out errorCode);

    /// <summary>校验 Agent Workflow 与 Checkpoint 的兼容性：Workflow 必须已注册、Checkpoint 格式版本必须等于当前版本、框架版本必须精确匹配。</summary>
    /// <param name="workflowKey">Workflow 稳定键。</param>
    /// <param name="workflowVersion">Workflow 版本号。</param>
    /// <param name="checkpointFormatVersion">Checkpoint 序列化格式版本号。</param>
    /// <param name="frameworkVersion">生成 Checkpoint 的 Agent 框架版本。</param>
    /// <param name="errorCode">校验失败时输出的稳定错误码；成功时为空字符串。</param>
    /// <returns>全部兼容返回 true；否则返回 false 并通过 errorCode 暴露失败原因。</returns>
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
