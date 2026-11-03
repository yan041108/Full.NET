namespace Full.NET.Modules.Ai.Persistence;

/// <summary>扫描只携带清理标识和可信范围；Kind 为索引墓碑 0、文件补偿 1、退休代次 2 或校对来源墓碑 3。</summary>
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
