namespace Full.NET.Modules.Ai.Persistence;

/// <summary>由不可变 FAQ 修订派生的真实数据库来源；Id 同时为知识版本身份，不含 FileId。</summary>
internal sealed record AiKnowledgeFaqIndexSourceRecord
{
    public Guid Id { get; init; }
    public Guid FaqId { get; init; }
    public Guid RevisionId { get; init; }
    public string RevisionHash { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public string DataClassification { get; init; } = "";
    public int KnowledgeBaseVersion { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public string ModelFingerprint { get; init; } = "";
    public string BodyText { get; init; } = "";
    public string BodyHash { get; init; } = "";
    public long SizeBytes { get; init; }
    public Guid RequestKey { get; init; }
    public string RequestHash { get; init; } = "";
    public Guid ActorUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
