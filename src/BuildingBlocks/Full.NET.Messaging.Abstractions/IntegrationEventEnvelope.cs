namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 跨 Outbox、CDC、Kafka 与 Inbox 保持不变的可靠集成事件 Envelope V2。
/// 字段顺序、命名与语义一旦发布即视为长期契约，任何变更必须同步提升 <see cref="SchemaVersion"/>。
/// 所有构造路径均通过 <see cref="Create"/> 进行契约校验，避免非法载荷进入投递链路。
/// </summary>
public sealed class IntegrationEventEnvelope
{
    /// <summary>
    /// 事件全局唯一标识；幂等去重、死信溯源与切流 Cutoff 均以本字段为稳定锚点。
    /// </summary>
    public Guid EventId { get; }

    /// <summary>
    /// 稳定事件契约类型名（如 <c>UserRoleGranted</c>）；与 SchemaVersion 共同决定反序列化目标类。
    /// </summary>
    public string MessageType { get; }

    /// <summary>
    /// 事件契约结构版本号，从 1 开始；字段增删改必须递增本值并保留旧版反序列化兼容至少一个版本窗口。
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// 事件载荷序列化格式；当前实现仅接受 <c>application/x-messagepack</c> 以保证压缩与二进制兼容。
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// 触发事件的业务租户 ID；宿主级全局事件为 null。消费者需按本值切换租户作用域再调用 Handler。
    /// </summary>
    public Guid? TenantId { get; }

    /// <summary>
    /// Kafka 分区键；同一聚合根或业务实体相关事件必须使用相同 PartitionKey 以保证分区内顺序。
    /// </summary>
    public string PartitionKey { get; }

    /// <summary>
    /// 分布式关联 ID，承载跨服务请求追踪链路；可透传自上游 HTTP Header 或消息系统标识。
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// 直接引起本事件的前序命令/事件 ID；用于构建因果链与回溯故障根因。
    /// </summary>
    public Guid? CausationId { get; }

    /// <summary>
    /// W3C Trace Context traceparent 字符串；跨 Kafka 投递时保持 Activity 链路连续。
    /// </summary>
    public string? TraceParent { get; }

    /// <summary>
    /// 生产者模块标识（如 <c>identity-service</c>）；用于 DLQ 审计与跨团队责任定位。
    /// </summary>
    public string Producer { get; }

    /// <summary>
    /// 事件在业务侧实际发生的 UTC 时间；与 Kafka ProduceTimestamp 区分，允许 CDC 回填历史事件。
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// 只读事件载荷字节；当前使用 MessagePack 序列化，禁止直接写入 JSON 明文。
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    private IntegrationEventEnvelope(
        Guid eventId,
        string messageType,
        int schemaVersion,
        string contentType,
        Guid? tenantId,
        string partitionKey,
        string? correlationId,
        Guid? causationId,
        string? traceParent,
        string producer,
        DateTimeOffset occurredAtUtc,
        ReadOnlyMemory<byte> payload)
    {
        EventId = eventId;
        MessageType = messageType;
        SchemaVersion = schemaVersion;
        ContentType = contentType;
        TenantId = tenantId;
        PartitionKey = partitionKey;
        CorrelationId = correlationId;
        CausationId = causationId;
        TraceParent = traceParent;
        Producer = producer;
        OccurredAtUtc = occurredAtUtc;
        Payload = payload;
    }

    /// <summary>
    /// 校验并构造 Envelope；不暴露 Broker SDK 类型，仅承载稳定契约字段。
    /// </summary>
    public static IntegrationEventEnvelope Create(
        Guid eventId,
        string messageType,
        int schemaVersion,
        string contentType,
        Guid? tenantId,
        string partitionKey,
        string? correlationId,
        Guid? causationId,
        string? traceParent,
        string producer,
        DateTimeOffset occurredAtUtc,
        ReadOnlyMemory<byte> payload)
    {
        ValidateMessageType(messageType);
        ValidateSchemaVersion(schemaVersion);
        ValidateContentType(contentType);
        IntegrationEventMetadata.ValidatePartitionKey(partitionKey);
        IntegrationEventMetadata.ValidateProducer(producer);
        IntegrationEventMetadata.ValidateCorrelationId(correlationId);
        ValidateTraceParent(traceParent);

        if (payload.IsEmpty)
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.PayloadRequired,
                nameof(payload));
        }

        return new IntegrationEventEnvelope(
            eventId,
            messageType,
            schemaVersion,
            contentType,
            tenantId,
            partitionKey,
            correlationId,
            causationId,
            traceParent,
            producer,
            occurredAtUtc,
            payload);
    }

    internal static void ValidateMessageType(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType)
            || messageType.Length > MessagingNames.MessageTypeMaxLength
            || !MessagingNames.MessageTypePattern.IsMatch(messageType))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.MessageTypeInvalid,
                nameof(messageType));
        }
    }

    internal static void ValidateSchemaVersion(int schemaVersion)
    {
        if (schemaVersion < 1)
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.SchemaVersionInvalid,
                nameof(schemaVersion));
        }
    }

    internal static void ValidateContentType(string contentType)
    {
        if (!string.Equals(
            contentType,
            MessagingNames.ContentTypeMessagePack,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ContentTypeInvalid,
                nameof(contentType));
        }
    }

    internal static void ValidateTraceParent(string? traceParent)
    {
        if (traceParent is null)
        {
            return;
        }

        if (traceParent.Length > MessagingNames.TraceParentMaxLength
            || !MessagingNames.TraceParentPattern.IsMatch(traceParent))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.TraceParentInvalid,
                nameof(traceParent));
        }
    }
}