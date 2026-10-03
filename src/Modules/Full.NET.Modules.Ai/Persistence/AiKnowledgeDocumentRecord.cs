namespace Full.NET.Modules.Ai.Persistence;

/// <summary>文档元数据投影；删除与可信范围在 SQL 过滤，不向调用方泄露。</summary>
internal sealed class AiKnowledgeDocumentRecord
{
    public Guid Id { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public int Version { get; init; }
}
