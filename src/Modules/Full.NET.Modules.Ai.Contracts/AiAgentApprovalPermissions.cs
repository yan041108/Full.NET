namespace Full.NET.Modules.Ai.Contracts;

/// <summary>Agent 审批与委托权限码。</summary>
public static class AiAgentApprovalPermissions
{
    /// <summary>读取本人发起的审批请求。</summary>
    public const string Read = "ai.agent_approvals.read";

    /// <summary>创建写工具审批请求。</summary>
    public const string Request = "ai.agent_approvals.request";

    /// <summary>审批或拒绝写工具请求。</summary>
    public const string Decide = "ai.agent_approvals.decide";

    /// <summary>创建与撤销持久委托。</summary>
    public const string Delegate = "ai.agent_delegations.manage";
}
