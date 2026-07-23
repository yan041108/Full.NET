namespace Full.NET.Data.Abstractions;

/// <summary>
/// 抽象 Outbox 消息的租约领取、成功确认、重试与死信状态持久化。
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// 领取一批待处理消息，并为当前消费者写入有限期租约。
    /// </summary>
    /// <param name="batchSize">本轮最多领取的消息数。</param>
    /// <param name="lease">租约持续时间；到期后其他 Worker 可重新领取。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>当前成功领取的消息集合。</returns>
    Task<IReadOnlyList<OutboxEnvelope>> AcquireAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken);

    /// <summary>
    /// 将已成功处理的消息标记为完成，并释放租约。
    /// </summary>
    /// <param name="id">消息标识。</param>
    /// <param name="lockId">当前租约标识。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    Task MarkProcessedAsync(
        Guid id,
        Guid lockId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 将临时失败的消息释放回队列，并记录下次允许重试的时间。
    /// </summary>
    /// <param name="id">消息标识。</param>
    /// <param name="lockId">当前租约标识。</param>
    /// <param name="error">失败摘要；用于人工排障。</param>
    /// <param name="nextAttemptAt">下次允许重新领取的 UTC 时间。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    Task MarkFailedAsync(
        Guid id,
        Guid lockId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// 将永久失败或超过最大尝试次数的消息写入死信终态，并释放租约。
    /// </summary>
    /// <remarks>
    /// 死信消息仍保留原始 Payload、失败摘要和原因码，供人工审计与受控重放使用。
    /// </remarks>
    /// <param name="id">消息标识。</param>
    /// <param name="lockId">当前租约标识。</param>
    /// <param name="error">失败摘要；用于人工排障。</param>
    /// <param name="deadLetterReasonCode">稳定原因码；用于查询、审计和运维文档。</param>
    /// <param name="deadLetteredAt">进入死信终态的 UTC 时间。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    Task MarkDeadLetterAsync(
        Guid id,
        Guid lockId,
        string error,
        string deadLetterReasonCode,
        DateTimeOffset deadLetteredAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// 收敛 Outbox 死信路径的稳定原因码，避免把异常类型名直接当作持久化契约。
/// </summary>
public static class OutboxDeadLetterReasons
{
    /// <summary>消息 ContentType 不是当前 Worker 支持的线格式。</summary>
    public const string UnsupportedContentType = "outbox.unsupported_content_type";

    /// <summary>找不到当前消息类型与 SchemaVersion 对应的唯一处理器。</summary>
    public const string HandlerNotFound = "outbox.handler_not_found";

    /// <summary>同一路由存在多个处理器，无法确定唯一消费方。</summary>
    public const string AmbiguousHandler = "outbox.ambiguous_handler";

    /// <summary>载荷损坏或反序列化失败，继续重试无法恢复。</summary>
    public const string InvalidPayload = "outbox.invalid_payload";

    /// <summary>瞬时失败累计达到上限，需人工介入后再重放。</summary>
    public const string MaxAttemptsExceeded = "outbox.max_attempts_exceeded";
}

public sealed class OutboxConcurrencyException(Guid id, Guid lockId)
    : InvalidOperationException(
        $"Outbox message '{id:D}' is no longer owned by lock '{lockId:D}'.");
