namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 聊天消息持久化行。</summary>
internal sealed class AiChatMessageRecord
{
    /// <summary>消息标识。</summary>
    public Guid Id { get; init; }

    /// <summary>会话标识。</summary>
    public Guid SessionId { get; init; }

    /// <summary>角色键。</summary>
    public string RoleKey { get; init; } = string.Empty;

    /// <summary>消息正文。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>状态键。</summary>
    public string StatusKey { get; init; } = string.Empty;

    /// <summary>独立派生标记；空或损坏依赖也不能降级为普通消息。</summary>
    public bool IsKnowledgeAnswer { get; init; }

    /// <summary>绑定消息与可信范围的完整来源元数据，不得直接返回客户端。</summary>
    public string? KnowledgeLineage { get; init; }

    /// <summary>提示 Token 数。</summary>
    public int? PromptTokens { get; init; }

    /// <summary>补全 Token 数。</summary>
    public int? CompletionTokens { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
}
