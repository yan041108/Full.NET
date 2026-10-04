namespace Full.NET.Modules.Ai.Persistence;

/// <summary>索引任务冻结的后台会话；租约时间仅来自关联任务，不保存访问令牌。</summary>
internal sealed record AiKnowledgeIndexSessionRecord
{
    public Guid Id { get; init; }
    public Guid IndexTaskId { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid SessionId { get; init; }
    public string SessionKind { get; init; } = "";
    public string SecurityStamp { get; init; } = "";
    public string ActorScope { get; init; } = "";
    public string EffectiveScope { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
}
