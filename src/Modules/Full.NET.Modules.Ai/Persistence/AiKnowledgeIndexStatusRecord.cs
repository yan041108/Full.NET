namespace Full.NET.Modules.Ai.Persistence;

/// <summary>无正文的权威状态快照；内部活动令牌仅用于核对完成证明，禁止序列化到 HTTP。</summary>
internal sealed record AiKnowledgeIndexStatusRecord
{
    /// <summary>当前发布资格身份；空值仅用于管理侧候选证明。</summary>
    public Guid? PublicationId { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid OwnerUserId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    /// <summary>当前权威分类；分类不是模型派发批准，空值须在处理准入时拒绝。</summary>
    public string DataClassification { get; init; } = string.Empty;
    /// <summary>知识库明确批准的生成配置；空值表示尚未批准。</summary>
    public Guid? GenerationModelConfigId { get; init; }
    /// <summary>生成批准的精确配置版本；必须与配置标识同时存在。</summary>
    public int? GenerationModelVersion { get; init; }
    public int IsDeleted { get; init; }
    public int CleanupPending { get; init; }
    public int HasDerivedData { get; init; }
    public Guid? TaskId { get; init; }
    /// <summary>来自任务冻结身份的人工修订标识；不能按 MIME 或可损坏的清单格式猜测来源协议。</summary>
    public Guid? CorrectionRevisionId { get; init; }
    public string? StateKey { get; init; }
    public int IsCurrent { get; init; }
    public Guid? ActiveTaskId { get; init; }
    public Guid? ActiveLeaseToken { get; init; }
    public string? ActiveReceiptHash { get; init; }
    public string? ActiveManifestHash { get; init; }
    public string? ActivePlanHash { get; init; }
    /// <summary>真实文件引用；FAQ 数据库修订来源为空，禁止伪造文件。</summary>
    public Guid? FileId { get; init; }
    /// <summary>静态数据库修订来源，与 FileId 严格二选一。</summary>
    public Guid? FaqRevisionId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string ParsingPolicyKey { get; init; } = "default";
    public long SizeBytes { get; init; }
    public Guid? ModelConfigId { get; init; }
    public int? ModelConfigVersion { get; init; }
    public string? ProviderKey { get; init; }
    public string? ModelId { get; init; }
}
