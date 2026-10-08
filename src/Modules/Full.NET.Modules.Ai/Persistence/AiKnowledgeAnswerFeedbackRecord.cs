namespace Full.NET.Modules.Ai.Persistence;

/// <summary>本人反馈行；仅含受控标签及来源证明摘要，不保存正文。</summary>
internal sealed class AiKnowledgeAnswerFeedbackRecord
{
    /// <summary>应用端 UUID v7。</summary>
    public Guid Id { get; init; }
    /// <summary>实际消息标识。</summary>
    public Guid MessageId { get; init; }
    /// <summary>评价键，撤回时清除。</summary>
    public string? RatingKey { get; init; }
    /// <summary>原因键，撤回时清除。</summary>
    public string? ReasonKey { get; init; }
    /// <summary>真实引用标识的规范 JSON。</summary>
    public string CitationIdsJson { get; init; } = "[]";
    /// <summary>原始完整依赖 JSON 的摘要，撤回时清除。</summary>
    public string? LineageHash { get; init; }
    /// <summary>pending_review 或 withdrawn。</summary>
    public string StatusKey { get; init; } = "";
    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
    /// <summary>最后变更 UTC。</summary>
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
