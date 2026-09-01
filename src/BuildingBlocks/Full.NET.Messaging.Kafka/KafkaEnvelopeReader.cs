using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka 信封头名称，与 Debezium Outbox 路由规范第 6.3 节保持一致。
/// </summary>
public static class KafkaEnvelopeHeaderNames
{
    /// <summary>事件唯一标识（UUID v7 字符串）。</summary>
    public const string EventId = "event_id";
    /// <summary>事件 CLR FullName 或等价稳定类型标识。</summary>
    public const string MessageType = "message_type";
    /// <summary>事件载荷 Schema 版本号（十进制字符串）。</summary>
    public const string SchemaVersion = "schema_version";
    /// <summary>载荷 Content-Type：如 application/x-memorypack。</summary>
    public const string ContentType = "content_type";
    /// <summary>租户 Id（UUID D 字符串）；Host 级事件时省略。</summary>
    public const string TenantId = "tenant_id";
    /// <summary>业务关联 Id；用于链路跨请求串联。</summary>
    public const string CorrelationId = "correlation_id";
    /// <summary>因果 Id；表示触发本事件的上游事件 MessageId。</summary>
    public const string CausationId = "causation_id";
    /// <summary>W3C TraceContext traceparent 头；用于分布式追踪关联。</summary>
    public const string TraceParent = "trace_parent";
    /// <summary>生产者稳定标识；如 host/worker 的逻辑名。</summary>
    public const string Producer = "producer";
    /// <summary>事件发生时间（UTC，ISO 8601）。</summary>
    public const string OccurredAtUtc = "occurred_at_utc";
}

/// <summary>
/// Maps Confluent ConsumeResult into IntegrationEventEnvelope without exposing broker offsets.
/// </summary>
public sealed class KafkaEnvelopeReader
{
    public bool TryRead(
        ConsumeResult<string, byte[]> consumeResult,
        out IntegrationEventEnvelope? envelope,
        out string? failureCode)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        envelope = null;
        failureCode = null;

        if (consumeResult.Message is null)
        {
            failureCode = IntegrationEventFailureCodes.PayloadRequired;
            return false;
        }

        var rawPayload = consumeResult.Message.Value;
        if (rawPayload is null)
        {
            failureCode = IntegrationEventFailureCodes.PayloadRequired;
            return false;
        }

        var payload = KafkaConnectPayloadNormalizer.Normalize(rawPayload);
        if (payload.IsEmpty)
        {
            failureCode = IntegrationEventFailureCodes.PayloadRequired;
            return false;
        }

        if (!KafkaEnvelopeHeaderParsers.TryParsePartitionKey(
                consumeResult.Message.Key,
                out var partitionKey))
        {
            failureCode = IntegrationEventFailureCodes.PartitionKeyRequired;
            return false;
        }

        if (!TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.EventId, out var eventIdText)
            || !KafkaEnvelopeHeaderParsers.TryParseGuidHeader(eventIdText, out var eventId))
        {
            failureCode = IntegrationEventFailureCodes.ContractPrefix + "event_id_invalid";
            return false;
        }

        if (!TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.MessageType, out var messageType))
        {
            failureCode = IntegrationEventFailureCodes.MessageTypeInvalid;
            return false;
        }

        if (!TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.SchemaVersion, out var schemaVersionText)
            || !int.TryParse(schemaVersionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schemaVersion))
        {
            failureCode = IntegrationEventFailureCodes.SchemaVersionInvalid;
            return false;
        }

        if (!TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.ContentType, out var contentType))
        {
            failureCode = IntegrationEventFailureCodes.ContentTypeInvalid;
            return false;
        }

        if (!TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.Producer, out var producer))
        {
            failureCode = IntegrationEventFailureCodes.ProducerInvalid;
            return false;
        }

        if (!TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.OccurredAtUtc, out var occurredAtText)
            || !KafkaEnvelopeHeaderParsers.TryParseOccurredAtUtc(occurredAtText, out var occurredAtUtc))
        {
            failureCode = IntegrationEventFailureCodes.ContractPrefix + "occurred_at_invalid";
            return false;
        }

        Guid? tenantId = null;
        if (TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.TenantId, out var tenantIdText)
            && !string.IsNullOrWhiteSpace(tenantIdText))
        {
            if (!KafkaEnvelopeHeaderParsers.TryParseGuidHeader(tenantIdText, out var parsedTenantId))
            {
                failureCode = IntegrationEventFailureCodes.ContractPrefix + "tenant_id_invalid";
                return false;
            }

            tenantId = parsedTenantId;
        }

        string? correlationId = null;
        if (TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.CorrelationId, out var correlationIdText)
            && !string.IsNullOrWhiteSpace(correlationIdText))
        {
            correlationId = correlationIdText;
        }

        Guid? causationId = null;
        if (TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.CausationId, out var causationIdText)
            && !string.IsNullOrWhiteSpace(causationIdText))
        {
            if (!KafkaEnvelopeHeaderParsers.TryParseGuidHeader(causationIdText, out var parsedCausationId))
            {
                failureCode = IntegrationEventFailureCodes.ContractPrefix + "causation_id_invalid";
                return false;
            }

            causationId = parsedCausationId;
        }

        string? traceParent = null;
        if (TryGetHeader(consumeResult.Message.Headers, KafkaEnvelopeHeaderNames.TraceParent, out var traceParentText)
            && !string.IsNullOrWhiteSpace(traceParentText))
        {
            traceParent = traceParentText;
        }

        try
        {
            envelope = IntegrationEventEnvelope.Create(
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
            return true;
        }
        catch (ArgumentException exception)
        {
            failureCode = exception.Message;
            var parameterSuffixIndex = failureCode.IndexOf(" (Parameter", StringComparison.Ordinal);
            if (parameterSuffixIndex >= 0)
            {
                failureCode = failureCode[..parameterSuffixIndex];
            }
            envelope = null;
            return false;
        }
    }

    private static bool TryGetHeader(
        Headers? kafkaHeaders,
        string key,
        out string value)
    {
        value = string.Empty;
        if (kafkaHeaders is null)
        {
            return false;
        }

        var header = kafkaHeaders.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));
        if (header is null || header.GetValueBytes() is null)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(header.GetValueBytes());
        return !string.IsNullOrWhiteSpace(value);
    }
}
