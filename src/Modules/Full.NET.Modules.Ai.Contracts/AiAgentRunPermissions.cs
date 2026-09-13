namespace Full.NET.Modules.Ai.Contracts;

/// <summary>持久 Agent 运行管理权限码。</summary>
public static class AiAgentRunPermissions
{
    /// <summary>读取本人 Agent 运行状态。</summary>
    public const string Read = "ai.agent_runs.read";

    /// <summary>创建持久 Agent 运行。</summary>
    public const string Create = "ai.agent_runs.create";

    /// <summary>取消本人 Agent 运行。</summary>
    public const string Cancel = "ai.agent_runs.cancel";

    /// <summary>在审批后恢复 awaiting_approval 运行。</summary>
    public const string Resume = "ai.agent_runs.resume";
}
