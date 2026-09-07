using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>按会话、主体、租户及生成标识维护跨实例租约，旧任务不得覆盖新一代状态。</summary>
internal static class AiChatGenerationSql
{
    private const string OwnerScope = """
        Id = @SessionId AND OwnerUserId = @OwnerUserId
        AND ((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId = @ScopeTenantId)
        """;

    /// <summary>原子获取空闲或已到期生成槽位；同一时刻只允许一个请求写入消息。</summary>
    public static readonly SqlStatement Acquire = new(
        "ai.acquire_chat_generation",
        $"""
        UPDATE fn_ai_chat_session
        SET IsGenerating = 1, GenerationId = @GenerationId,
            GenerationExpiresAtUtc = @ExpiresAtUtc, GenerationCancellationRequested = 0,
            UpdatedAtUtc = @Now, Version = Version + 1
        WHERE {OwnerScope}
          AND (IsGenerating = 0 OR GenerationExpiresAtUtc <= @Now OR GenerationExpiresAtUtc IS NULL)
        """, SqlDataScope.Global);

    /// <summary>只续租未过期且未取消的当前一代；返回零时必须中止提供程序调用。</summary>
    public static readonly SqlStatement Renew = new(
        "ai.renew_chat_generation",
        $"""
        UPDATE fn_ai_chat_session SET GenerationExpiresAtUtc = @ExpiresAtUtc
        WHERE {OwnerScope} AND IsGenerating = 1 AND GenerationId = @GenerationId
          AND GenerationExpiresAtUtc > @Now AND GenerationCancellationRequested = 0
        """, SqlDataScope.Global);

    /// <summary>只释放自身仍持有的生成槽位，不能清除后继任务。</summary>
    public static readonly SqlStatement Release = new(
        "ai.release_chat_generation",
        $"""
        UPDATE fn_ai_chat_session
        SET IsGenerating = 0, GenerationId = NULL, GenerationExpiresAtUtc = NULL,
            GenerationCancellationRequested = 0, UpdatedAtUtc = @Now, Version = Version + 1
        WHERE {OwnerScope} AND GenerationId = @GenerationId
        """, SqlDataScope.Global);

    /// <summary>持久化取消请求，使其他 API 实例的续租轮询也能停止生成。</summary>
    public static readonly SqlStatement RequestCancellation = new(
        "ai.request_chat_generation_cancellation",
        $"""
        UPDATE fn_ai_chat_session SET GenerationCancellationRequested = 1
        WHERE {OwnerScope} AND GenerationId = @GenerationId AND IsGenerating = 1
        """, SqlDataScope.Global);
    /// <summary>在取得会话写锁后收敛被崩溃进程留下的消息，不自动重放外部推理。</summary>
    public static readonly SqlStatement FailAbandonedMessages = new(
        "ai.fail_abandoned_chat_messages",
        """
        UPDATE fn_ai_chat_message SET StatusKey = 'failed'
        WHERE SessionId = @SessionId AND StatusKey = 'streaming'
        """, SqlDataScope.Global);
}
