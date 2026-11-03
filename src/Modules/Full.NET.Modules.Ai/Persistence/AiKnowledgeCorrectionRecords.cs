namespace Full.NET.Modules.Ai.Persistence;

/// <summary>授权查询取得的原解析与初始修订载荷；读取后还须核验摘要和协议。</summary>
internal sealed record AiKnowledgeCorrectionSourceRecord(Guid Id, Guid VersionId, Guid RequestKey, string RequestHash,
    string FileHash, string ParserRuntimeId, string SourceHash, byte[] OriginalPayload, byte[] InitialPayload,
    string InitialPayloadHash, Guid ActorUserId, DateTimeOffset CreatedAtUtc);
/// <summary>不可变人工修订和审计；不能据此推断当前发布或读取资格。</summary>
internal sealed record AiKnowledgeCorrectionRevisionRecord(Guid Id, Guid VersionId, Guid SourceId, Guid? ParentId,
    Guid RequestKey, string RequestHash, byte[] Payload, string PayloadHash, string ReasonText, Guid ActorUserId,
    DateTimeOffset CreatedAtUtc, int DocumentVersion, int HeadVersion);
/// <summary>独立维护版本，首次解析尚无人工修订。</summary>
internal sealed record AiKnowledgeCorrectionHeadRecord(Guid Id, Guid SourceId, Guid? RevisionId, int Version);
/// <summary>历史目录只取审计元数据，不能在分页时读入所有修订正文。</summary>
internal sealed record AiKnowledgeCorrectionRevisionSummaryRecord(Guid Id, Guid VersionId, Guid SourceId, Guid? ParentId,
    int HeadVersion, int DocumentVersion, string PayloadHash, string ReasonText, DateTimeOffset CreatedAtUtc);
