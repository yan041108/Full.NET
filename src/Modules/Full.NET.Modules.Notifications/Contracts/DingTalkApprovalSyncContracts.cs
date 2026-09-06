namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>钉钉审批镜像同步的稳定状态机码；只描述镜像记录，不驱动 Full.NET Workflow 决策。</summary>
public static class DingTalkApprovalSyncStatusKeys
{
    /// <summary>已登记，等待向钉钉发起审批实例。</summary>
    public const string PendingOutbound = "pending_outbound";

    /// <summary>出站创建失败，可补偿重试。</summary>
    public const string OutboundFailed = "outbound_failed";

    /// <summary>钉钉审批进行中。</summary>
    public const string Running = "running";

    /// <summary>钉钉审批已完成。</summary>
    public const string Completed = "completed";

    /// <summary>钉钉审批已撤销/终止。</summary>
    public const string Terminated = "terminated";
}

/// <summary>钉钉审批镜像同步的独立稳定权限码。</summary>
public static class DingTalkApprovalSyncPermissions
{
    /// <summary>查询同步记录列表与详情。</summary>
    public const string Read = "notifications.dingtalk_approval_sync.read";

    /// <summary>为工作流实例登记镜像同步。</summary>
    public const string Create = "notifications.dingtalk_approval_sync.create";

    /// <summary>对失败出站记录发起补偿重试。</summary>
    public const string Retry = "notifications.dingtalk_approval_sync.retry";
}

/// <summary>登记钉钉审批镜像同步请求；Full.NET Workflow 仍为流程权威。</summary>
/// <param name="WorkflowInstanceId">本地工作流实例标识。</param>
/// <param name="OriginatorUserId">钉钉发起人 userId。</param>
/// <param name="DeptId">钉钉部门标识。</param>
/// <param name="Title">审批标题，映射为表单组件值。</param>
/// <param name="Summary">审批摘要，可选。</param>
public sealed record CreateDingTalkApprovalSyncRequest(
    Guid WorkflowInstanceId,
    string OriginatorUserId,
    long DeptId,
    string Title,
    string? Summary);

/// <summary>钉钉审批镜像同步记录响应。</summary>
public sealed record DingTalkApprovalSyncResponse(
    Guid Id,
    Guid WorkflowInstanceId,
    string IdempotencyKey,
    string? DingTalkProcessInstanceId,
    string ProcessCode,
    string OriginatorUserId,
    long DeptId,
    string Title,
    string? Summary,
    string StatusKey,
    string? ExternalStatusKey,
    string? ExternalResultKey,
    string? LastErrorCode,
    DateTimeOffset? LastSyncedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>匿名回调受理响应；只确认镜像记录已更新。</summary>
public sealed record DingTalkApprovalSyncCallbackAcceptedResponse(
    Guid SyncRecordId,
    string StatusKey);
