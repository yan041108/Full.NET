namespace Full.NET.Modules.Workflow.Domain;

/// <summary>恢复任务种类；扫描器只创建这三类闭合键。</summary>
internal static class WorkflowRecoveryKinds
{
    /// <summary>活动实例上的执行租约已过期。</summary>
    public const string ExpiredLease = "expired_lease";

    /// <summary>活动实例没有未过期租约，也没有活动待办。</summary>
    public const string StuckInstance = "stuck_instance";

    /// <summary>活动人工审批步骤缺少对应活动待办。</summary>
    public const string IncompleteStep = "incomplete_step";
}

/// <summary>恢复任务持久化状态；租约占用由 Lease 列表达，不另设 leased 状态。</summary>
internal static class WorkflowRecoveryStatuses
{
    /// <summary>等待 Worker 领取。</summary>
    public const string Pending = "pending";

    /// <summary>本次处理成功，源条件已消失或已修复。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>可重试失败，等待 NextAttemptAtUtc。</summary>
    public const string Failed = "failed";

    /// <summary>超过最大尝试次数，需人工重试或 reconcile。</summary>
    public const string DeadLettered = "dead_lettered";

    /// <summary>源实例已终态，任务关闭且不再占用。</summary>
    public const string Cancelled = "cancelled";
}
