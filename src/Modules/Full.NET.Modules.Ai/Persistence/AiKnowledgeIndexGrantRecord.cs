namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 自有授权及可选任务累计额度投影；原登录绑定不进入公开响应。</summary>
internal sealed record AiKnowledgeIndexGrantRecord
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid OwnerUserId { get; init; }
    public Guid ClientRequestId { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public string SessionJson { get; init; } = "";
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public int DocumentLimit { get; init; }
    public int AdmittedDocuments { get; init; }
    public long InputTokenLimitPerDocument { get; init; }
    public bool IsRevoked { get; init; }
    public int Version { get; init; }
    public Guid? TaskId { get; init; }
    public int LastBatchOrdinal { get; init; } = -1;
    public long ReservedInputTokens { get; init; }
}

/// <summary>全局扫描只提供新可信作用域的最小锚点，不构成用户授权。</summary>
internal sealed record AiKnowledgeIndexGrantAnchor(Guid Id, Guid? TenantId);
