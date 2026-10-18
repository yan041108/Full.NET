namespace Full.NET.Modules.Ai.Persistence;

/// <summary>一次派发判定点的来源及模型绑定；不包含正文或凭据，也不替代预算和数据去向策略。</summary>
internal sealed class AiKnowledgeIndexDispatchRecord
{
    public Guid TaskId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public Guid LeaseToken { get; init; }
    public DateTimeOffset LeaseExpiresAtUtc { get; init; }
    /// <summary>最终查询时的原增量授权期限；手工任务为空，查询等待后仍须复核真实时钟。</summary>
    public DateTimeOffset? GrantExpiresAtUtc { get; init; }
    public Guid FileId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    // 同一 AI 判定点投影分类和实际模型绑定，避免后续独立查询留下末尾漂移窗口；不含凭据。
    public string DataClassification { get; init; } = string.Empty;
    public string ModelProviderKey { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string ModelEndpointBaseUrl { get; init; } = string.Empty;
    public string? ModelOrganizationId { get; init; }
}
