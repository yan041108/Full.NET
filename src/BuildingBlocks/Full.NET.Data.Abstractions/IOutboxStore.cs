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
    /// 延长当前批次仍未进入终态的消息租约，防止慢 Handler 与批尾消息被并发回收。
    /// </summary>
    /// <param name="messageIds">当前批次的精确消息标识集合。</param>
    /// <param name="lockId">当前批次共享的租约标识。</param>
    /// <param name="lease">从当前时刻开始计算的新租约持续时间。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    Task RenewLeaseAsync(
        IReadOnlyCollection<Guid> messageIds,
        Guid lockId,
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

    /// <summary>事件流已切离旧轮询所有权，Legacy Worker 不得再消费。</summary>
    public const string LegacyOwnerRevoked = "outbox.legacy_owner_revoked";
}

/// <summary>
/// 当 Outbox 状态更新因租约所有权变更而失败时抛出：当前 Worker 持有的 LockId
/// 已不再是目标消息行的所有者（租约到期被其他 Worker 抢占，或已被标记为终态）。
/// Relay Worker 捕获该异常后必须放弃当前批次的后续确认，避免重复发布。
/// </summary>
/// <remarks>
/// 典型触发路径：Relay 处理批次过慢 → 租约过期 → 另一个 Worker 的 AcquireAsync
/// 抢占并更新了 LockId → 当前 Worker 调用 MarkProcessed/MarkFailed 时
/// WHERE Id = @Id AND LockId = @LockId 条件不匹配 → 影响行数 0 → 抛出本异常。
/// 该异常是至少一次投递语义的固有组成部分，不代表系统故障，无需告警。
/// </remarks>
public sealed class OutboxConcurrencyException(Guid id, Guid lockId)
    : InvalidOperationException(
        $"Outbox message '{id:D}' is no longer owned by lock '{lockId:D}'.");

/// <summary>
/// 当 Outbox 批次级租约续期失败（整个 LockId 对应的所有行都已不再归属当前 Worker）
/// 时抛出。与 <see cref="OutboxConcurrencyException"/> 单条失败不同，本异常表示
/// 整个批次的租约已丢失，Worker 必须立即停止处理当前批次剩余消息。
/// </summary>
/// <remarks>
/// 典型触发路径：数据库分区切换 / 长 GC 停顿导致 RenewLeaseAsync 超时 → 整个批次
/// 租约过期。捕获本异常后应：1) 停止当前批次中未处理消息的发布；
/// 2) 记录 WARN 级别日志（单条 INFO，批量 WARN）；3) 等待下一轮 AcquireAsync
/// 重新领取后再处理，不得强行 MarkProcessed 导致丢失死信判断。
/// </remarks>
public sealed class OutboxLeaseLostException(Guid lockId)
    : InvalidOperationException(
        $"Outbox lease '{lockId:D}' is no longer owned.");
