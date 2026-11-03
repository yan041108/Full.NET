namespace Full.NET.Modules.Ai.Persistence;

/// <summary>不可变 FAQ 审核关联，不保存授权缓存或 Files 引用。</summary>
internal sealed record AiKnowledgeFaqReleaseRecord
{
    public Guid Id { get; init; }
    public Guid FaqId { get; init; }
    public Guid RevisionId { get; init; }
    public Guid VersionId { get; init; }
    public string SourceHash { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public string RequestHash { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>固定 FAQ 发布协议；源类型和准确修订参与审核摘要，不冒充文件来源快照。</summary>
internal sealed record AiKnowledgeFaqPublicationSnapshot(string Kind, Guid FaqId, Guid RevisionId, string ContentHash,
    string SourceHash, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, AiKnowledgeIndexStatusRecord Source);
