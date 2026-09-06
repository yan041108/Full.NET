namespace Full.NET.Modules.K3Cloud.Persistence;

/// <summary>K3Cloud 连接配置持久化行。</summary>
internal sealed class K3CloudConnectionConfigRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string AcctId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordProtected { get; set; } = string.Empty;
    public int Lcid { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? LastTestedAtUtc { get; set; }
    public string? LastTestStatusKey { get; set; }
    public string? LastTestMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

/// <summary>K3Cloud 单据同步持久化行。</summary>
internal sealed class K3CloudDocumentSyncRecord
{
    public Guid Id { get; set; }
    public Guid ConnectionConfigId { get; set; }
    public string DocumentTypeKey { get; set; } = string.Empty;
    public string BusinessKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public string? LastStepKey { get; set; }
    public string? ExternalBillId { get; set; }
    public string? ExternalBillNo { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public int Version { get; set; }
}
