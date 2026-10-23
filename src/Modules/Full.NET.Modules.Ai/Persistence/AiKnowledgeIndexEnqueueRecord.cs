namespace Full.NET.Modules.Ai.Persistence;

/// <summary>公开入队的当前 AI 权威快照；不读取原文、文件凭据或会话秘密。</summary>
internal sealed record AiKnowledgeIndexEnqueueRecord
{
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid OwnerUserId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string ParsingPolicyKey { get; init; } = "default";
}
