namespace Full.NET.Agents.Runtime;

/// <summary>自动恢复只针对明确提交的步骤，未知副作用不能猜测为可重试。</summary>
public static class AgentRunState
{
    /// <summary>
    /// 判断 Agent Run 状态机是否允许从 <paramref name="from"/> 迁移到 <paramref name="to"/>；
    /// 仅允许白名单内的合法迁移，其余返回 <see langword="false"/>。
    /// </summary>
    public static bool CanTransition(string from, string to) => (from, to) switch
    {
        ("queued", "running" or "cancelled" or "expired") => true,
        ("running", "completed" or "awaiting_approval" or "authorization_required" or "retry_scheduled"
            or "reconciliation_required" or "failed" or "cancelled" or "expired") => true,
        ("awaiting_approval", "queued") => true,
        _ => false
    };
    /// <summary>
    /// 根据步骤状态返回恢复动作：<c>null</c>→dispatch、<c>committed</c>→complete、
    /// <c>started</c>→reconcile；未知状态抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static string RecoveryAction(string? stepStatus) => stepStatus switch
    {
        null => "dispatch", "committed" => "complete", "started" => "reconcile",
        _ => throw new InvalidOperationException("Unknown agent step state.")
    };

    /// <summary>判断状态是否为终态（completed/failed/cancelled/expired），终态不可再迁移。</summary>
    public static bool IsTerminal(string status) => status is
        "completed" or "failed" or "cancelled" or "expired";

    /// <summary>判断状态是否允许提交步骤为 committed；包含运行中与所有终态/等待态，允许幂等提交。</summary>
    public static bool CanTransitionToCommitted(string status) => status is
        "running" or "completed" or "awaiting_approval" or "authorization_required"
        or "retry_scheduled" or "reconciliation_required" or "failed" or "cancelled" or "expired";
}
