namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 事件流交付所有权的持久化记录；用于切流、回退边界与有效所有权解析。
/// 本记录采用 CAS 乐观并发：<see cref="Version"/>（若实现）或 <see cref="UpdatedAtUtc"/> 作为一致性标签，
/// <see cref="PreviousOwner"/> 字段用于追踪切流历史以支持影子验证期与快速回退。
/// </summary>
public sealed record EventStreamOwnershipRecord(
    /// <summary>
    /// 集成事件的稳定契约类型名（如 <c>TenantCreated</c>），与 SchemaVersion 共同唯一标识一条事件流。
    /// </summary>
    string MessageType,

    /// <summary>
    /// 事件契约 Schema 版本，从 1 开始递增；字段结构变更必须同步提升版本号。
    /// </summary>
    int SchemaVersion,

    /// <summary>
    /// 逻辑 Topic 代码，由目录声明；实际 Kafka Topic 名称需通过 <c>KafkaTopicNames</c> 进一步规范化。
    /// </summary>
    string TopicCode,

    /// <summary>
    /// 当前生效的交付所有权；Producer/Consumer 必须据此决定 Outbox 写入路径与消费链路。
    /// </summary>
    EventDeliveryOwner CurrentOwner,

    /// <summary>
    /// 上一次生效的交付所有权；切流后保留用于快速回退判定，无需再次遍历变更日志。
    /// 从未发生过切流时与 <see cref="CurrentOwner"/> 相同。
    /// </summary>
    EventDeliveryOwner PreviousOwner,

    /// <summary>
    /// 本次切流截止事件 ID；新 Owner 仅消费大于该 ID 的事件，旧 Owner 仅负责等于小于该 ID 的残留投递。
    /// </summary>
    Guid CutoffEventId,

    /// <summary>
    /// 切流决策发生的 UTC 时间戳；用于 CDC Relay 与 Legacy Polling 对齐时间序列。
    /// </summary>
    DateTimeOffset CutoffOccurredAtUtc,

    /// <summary>
    /// CDC 源位点 JSON 快照（如 MySQL binlog file:position），切流时写入，切回原 Owner 时无需重新扫描整条流。
    /// </summary>
    string? CdcSourcePositionJson,

    /// <summary>
    /// 触发本次所有权变更的操作人用户 ID；系统自动切流为 null，便于审计区分人工与自动动作。
    /// </summary>
    Guid? OperatorUserId,

    /// <summary>
    /// 变更原因自由文本；影子验证、正式切换、回退等动作需写入可读说明供运维审计。
    /// </summary>
    string Reason,

    /// <summary>
    /// 回滚安全边界事件 ID；仅允许回滚到该事件之前的位点，避免回退时跳过已被消费者幂等表记录的事件。
    /// </summary>
    Guid? RollbackBoundaryEventId,

    /// <summary>
    /// 上一次回滚发生的 UTC 时间戳；多次回滚取最新值，用于限制短时间内反复切回造成的顺序抖动。
    /// </summary>
    DateTimeOffset? RollbackOccurredAtUtc,

    /// <summary>
    /// 记录首次创建的 UTC 时间戳；仅用于审计，不参与 CAS 判定。
    /// </summary>
    DateTimeOffset CreatedAtUtc,

    /// <summary>
    /// 记录最近更新的 UTC 时间戳；持久化层可据此做乐观并发检查（Compare-And-Swap）。
    /// </summary>
    DateTimeOffset UpdatedAtUtc);
