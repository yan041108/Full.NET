namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 跨 Outbox、CDC、Kafka 与 Inbox 保持不变的可靠集成事件 Envelope V2。
/// </summary>
public sealed class IntegrationEventEnvelope
{
    public Guid EventId { get; }

    public string MessageType { get; }

    public int SchemaVersion { get; }

    public string ContentType { get; }

    public Guid? TenantId { get; }

    public string PartitionKey { get; }

    public string? CorrelationId { get; }

    public Guid? CausationId { get; }

    public string? TraceParent { get; }

    public string Producer { get; }

    public DateTimeOffset OccurredAtUtc { get; }

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