namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>钉钉审批镜像同步记录投影。</summary>
internal sealed class DingTalkApprovalSyncRecord
{
    public Guid Id { get; init; }
    public string TenantScopeKey { get; init; } = string.Empty;
    public Guid WorkflowInstanceId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string? DingTalkProcessInstanceId { get; init; }
    public string ProcessCode { get; init; } = string.Empty;
    public string OriginatorUserId { get; init; } = string.Empty;
    public long DeptId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string StatusKey { get; init; } = string.Empty;
    public string? ExternalStatusKey { get; init; }
    public string? ExternalResultKey { get; init; }
    public string? LastErrorCode { get; init; }
    public DateTimeOffset? LastSyncedAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public Guid CreatedByUserId { get; init; }
}
