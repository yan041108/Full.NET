namespace Full.NET.Agents.Runtime;

/// <summary>自动恢复只针对明确提交的步骤，未知副作用不能猜测为可重试。</summary>
public static class AgentRunState
{
    public static bool CanTransition(string from, string to) => (from, to) switch
    {
        ("queued", "running" or "cancelled" or "expired") => true,
        ("running", "completed" or "awaiting_approval" or "authorization_required" or "retry_scheduled"
            or "reconciliation_required" or "failed" or "cancelled" or "expired") => true,
        ("awaiting_approval", "queued") => true,
        _ => false
    };
    public static string RecoveryAction(string? stepStatus) => stepStatus switch
    {
        null => "dispatch", "committed" => "complete", "started" => "reconcile",
        _ => throw new InvalidOperationException("Unknown agent step state.")
    };

    public static bool IsTerminal(string status) => status is
        "completed" or "failed" or "cancelled" or "expired";

    public static bool CanTransitionToCommitted(string status) => status is
        "running" or "completed" or "awaiting_approval" or "authorization_required"
        or "retry_scheduled" or "reconciliation_required" or "failed" or "cancelled" or "expired";
}
