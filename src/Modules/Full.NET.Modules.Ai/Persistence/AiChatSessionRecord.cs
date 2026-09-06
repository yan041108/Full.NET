namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 聊天会话持久化行。</summary>
internal sealed class AiChatSessionRecord
{
    /// <summary>会话标识。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>所属用户标识。</summary>
    public Guid OwnerUserId { get; init; }

    /// <summary>模型配置标识。</summary>
    public Guid ModelConfigId { get; init; }

    /// <summary>模型配置名称快照。</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>会话标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>消息条数。</summary>
    public int MessageCount { get; init; }

    /// <summary>最近消息时间。</summary>
    public DateTimeOffset? LastMessageAtUtc { get; init; }

    /// <summary>是否正在生成。</summary>
    public bool IsGenerating { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
}
