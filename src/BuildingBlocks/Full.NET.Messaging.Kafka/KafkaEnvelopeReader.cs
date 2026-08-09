using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Envelope header names aligned with Debezium Outbox routing (spec section 6.3).
/// </summary>
public static class KafkaEnvelopeHeaderNames
{
    public const string EventId = "event_id";
    public const string MessageType = "message_type";
    public const string SchemaVersion = "schema_version";
    public const string ContentType = "content_type";
    public const string TenantId = "tenant_id";
    public const string CorrelationId = "correlation_id";
    public const string CausationId = "causation_id";
    public const string TraceParent = "trace_parent";
    public const string Producer = "producer";
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

        var payload = consumeResult.Message.Value;
        if (payload is null || payload.Length == 0)
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
