namespace Full.NET.Modules.Ai.Persistence;

/// <summary>草稿根及当前不可变修订投影；原始来源列没有更新 SQL。</summary>
internal sealed record AiKnowledgeFaqDraftRecord
{
    public Guid Id { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid ReservedDocumentId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid CreatedById { get; init; }
    public Guid CreateRequestKey { get; init; }
    public string CreateHash { get; init; } = "";
    public string OriginKey { get; init; } = "";
    public string? Basis { get; init; }
    public string SourceJson { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public Guid HeadRevisionId { get; init; }
    public int HeadVersion { get; init; }
    public int RevisionNo { get; init; }
    public Guid RevisionId { get; init; }
    public Guid RequestKey { get; init; }
    public string RequestHash { get; init; } = "";
    public string ContentJson { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset RevisionCreatedAtUtc { get; init; }
    public Guid? RestoredFromRevisionId { get; init; }
}
