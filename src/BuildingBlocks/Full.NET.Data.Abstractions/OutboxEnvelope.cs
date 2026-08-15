namespace Full.NET.Data.Abstractions;

/// <summary>
/// Outbox 消息的不可变持久化信封：承载事件载荷、路由元数据与租约状态，
/// 是 <see cref="IOutboxStore"/> 与 Relay Worker 之间的传输契约。
/// </summary>
/// <remarks>
/// <para>
/// 生命周期：由 <see cref="IOutboxWriter"/> 在事务内创建行 → Relay Worker 通过
/// AcquireAsync 领取并更新 LockId / LeaseUntil → 成功发布后 MarkProcessedAsync
/// 软删除或物理保留 → 超过保留期后由 <see cref="IOutboxRetentionStore"/> 批量清理。
/// </para>
/// <para>
/// 并发安全：LockId 是单次领取的所有权凭证。任何 MarkProcessed / MarkFailed /
/// MarkDeadLetter 操作必须携带领取时返回的 LockId，实现层通过 WHERE LockId = @LockId
/// 条件原子更新；不匹配时抛出 <see cref="OutboxConcurrencyException"/>，说明租约
/// 已到期被其他 Worker 重新领取。
/// </para>
/// </remarks>
public sealed record OutboxEnvelope(
    /// <summary>
    /// 事件唯一标识，由 <see cref="IOutboxWriter"/> 写入时生成的 UUID v7。
    /// </summary>
    /// <remarks>
    /// 该 Id 同时是 Broker 端 MessageId 和 Inbox 端幂等键的一部分。
    /// UUID v7 的时间前缀保证 OccurredAtUtc 与 Id 的前缀排序一致。
    /// </remarks>
    Guid Id,

    /// <summary>
    /// 当前领取批次的租约标识；未领取时为 Guid.Empty。
    /// </summary>
    /// <remarks>
    /// 同一批次的所有消息共享一个 LockId，便于 <see cref="IOutboxStore.RenewLeaseAsync"/>
    /// 批量续期。实现层需保证 AcquireAsync 的 UPDATE ... OUTPUT 语句为整个批次
    /// 生成一致的 LockId。
    /// </remarks>
    Guid LockId,

    /// <summary>
    /// 规范化事件类型名（路由键），如 "fullnet.organization.unit.changed"。
    /// </summary>
    /// <remarks>
    /// 与 schemaVersion 共同决定 Handler 解析、CDC 切流门禁与积压统计粒度。
    /// </remarks>
    string MessageType,

    /// <summary>
    /// 事件结构版本正整数，从 1 开始单调递增。
    /// </summary>
    /// <remarks>
    /// 非向后兼容修改必须递增版本并保留旧 Handler 至少一个部署周期，
    /// 旧版本排空后方可移除（参见 <see cref="IOutboxBacklogReader.ReadVersionRetirementAsync"/>）。
    /// </remarks>
    int SchemaVersion,

    /// <summary>
    /// 载荷线格式标识，由 <see cref="IIntegrationEventSerializer.ContentType"/> 写入，
    /// 例如 "application/json;charset=utf-8"。
    /// </summary>
    /// <remarks>
    /// Relay Worker 在反序列化前必须校验 ContentType 匹配当前支持的格式集合；
    /// 不匹配时进入死信（参见 <see cref="OutboxDeadLetterReasons.UnsupportedContentType"/>）。
    /// </remarks>
    string ContentType,

    /// <summary>
    /// 产生该事件的租户 Id；Host 级全局事件为 null。
    /// </summary>
    /// <remarks>
    /// Relay 向多租户消费者分发时，会将该值映射到消息头 x-tenant-id，确保消费端
    /// 能够正确重建租户上下文，同时 Inbox 按 (tenant_id, message_id) 联合去重。
    /// </remarks>
    Guid? TenantId,

    /// <summary>
    /// 分布式追踪 TraceId，贯穿 Outbox 写入 → Relay 发布 → Broker → Inbox 消费全链路。
    /// </summary>
    /// <remarks>
    /// 格式为 W3C TraceContext trace-id（16 字节 hex，32 字符）。null 表示写入时
    /// 上游没有活动追踪，Relay 会在发布时生成新的 TraceId 并回填到 Broker 消息头。
    /// </remarks>
    string? TraceId,

    /// <summary>
    /// 事件载荷的原始序列化字节，长度不超过 1 MiB（由 IOutboxWriter 写入时校验）。
    /// </summary>
    /// <remarks>
    /// 大载荷应存储在对象存储中，Outbox 仅保留引用 URL，避免 WAL 膨胀与 Broker 消息限制。
    /// </remarks>
    byte[] Payload,

    /// <summary>
    /// 已尝试的发布次数，从 0 开始；每次 MarkFailedAsync 递增。
    /// </summary>
    /// <remarks>
    /// 超过实现层配置的 MaxAttempts（通常 5~10 次指数退避）后，Relay 会调用
    /// MarkDeadLetterAsync 进入死信终态，人工介入排障。
    /// </remarks>
    int Attempts,

    /// <summary>
    /// 事件发生的 UTC 时间，等于业务事务提交的时钟。
    /// </summary>
    /// <remarks>
    /// 该值是 Outbox 保留清理、切流门禁、时间窗口重放的基准时间，严格单调非递减
    /// （由数据库服务器时钟保证，不得使用客户端时钟）。
    /// </remarks>
    DateTimeOffset OccurredAtUtc);
