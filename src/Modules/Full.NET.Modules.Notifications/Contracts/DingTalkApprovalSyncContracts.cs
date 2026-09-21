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

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
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

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>钉钉审批镜像同步记录响应。</summary>
/// <param name="Id">同步记录稳定标识。</param>
/// <param name="WorkflowInstanceId">本地 Full.NET Workflow 实例标识；镜像同步的源。</param>
/// <param name="IdempotencyKey">幂等键；同一实例多次登记只产生一条镜像。</param>
/// <param name="DingTalkProcessInstanceId">钉钉审批实例标识；尚未创建时为 <see langword="null"/>。</param>
/// <param name="ProcessCode">钉钉审批模板编码；决定表单与审批节点布局。</param>
/// <param name="OriginatorUserId">钉钉发起人 userId；与本地发起人不一定相同。</param>
/// <param name="DeptId">钉钉部门标识；影响审批流路由。</param>
/// <param name="Title">审批标题；映射为表单组件值。</param>
/// <param name="Summary">审批摘要；可为空。</param>
/// <param name="StatusKey">镜像状态稳定机器码，取值自 DingTalkApprovalSyncStatusKeys。</param>
/// <param name="ExternalStatusKey">钉钉侧原始状态码；仅用于诊断，不可作为流程决策依据。</param>
/// <param name="ExternalResultKey">钉钉侧原始结果码；可空。</param>
/// <param name="LastErrorCode">最近同步失败时的稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="LastSyncedAtUtc">最近成功同步时间（UTC）；尚未同步时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">记录创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">记录最近更新时间（UTC）。</param>
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
/// <param name="SyncRecordId">镜像记录标识；与请求登记的记录对应。</param>
/// <param name="StatusKey">回调处理后记录的最新状态稳定机器码。</param>
public sealed record DingTalkApprovalSyncCallbackAcceptedResponse(
    Guid SyncRecordId,
    string StatusKey);
