namespace Full.NET.Modules.Ai.Persistence;

/// <summary>删除扫描只携带清理标识和可信范围；Kind 为 0（索引）或 1（文件补偿）。</summary>
internal sealed class AiKnowledgeCleanupRecord
{
    public Guid Id { get; init; }
    public int Kind { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? TenantId { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid OwnerUserId { get; init; }
}
