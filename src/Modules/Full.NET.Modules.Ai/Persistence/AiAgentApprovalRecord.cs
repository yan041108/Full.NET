namespace Full.NET.Modules.Ai.Persistence;

/// <summary>审批行映射；ArgumentsProtected 仅服务端消费，不进入 API 明文返回。</summary>
internal sealed class AiAgentApprovalRecord
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid OperationId { get; set; }
    public Guid SessionId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public int ToolVersion { get; set; }
    public string ArgumentsHash { get; set; } = string.Empty;
    public string ArgumentsProtected { get; set; } = string.Empty;
    public int PolicyVersion { get; set; }
    public string PresentationJson { get; set; } = string.Empty;
    public Guid RequestedBy { get; set; }
    public Guid? ApproverId { get; set; }
    public string DecisionKey { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
