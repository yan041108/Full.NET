namespace Full.NET.Modules.Ai.Persistence;

/// <summary>仅供后台扫描恢复作用域；不含正文、凭据或可缓存的派发授权。</summary>
internal sealed class AiKnowledgeIndexWorkerRecord
{
    public Guid TaskId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid OwnerUserId { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string ParsingPolicyKey { get; init; } = "default";
}
