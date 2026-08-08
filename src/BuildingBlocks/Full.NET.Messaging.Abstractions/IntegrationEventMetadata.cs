using System.Text;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Outbox 写入时由业务模块显式提供的分区、生产者与关联元数据。
/// </summary>
public sealed class IntegrationEventMetadata
{
    public string PartitionKey { get; }

    public string Producer { get; }

    public string? CorrelationId { get; }

    public Guid? CausationId { get; }

    private IntegrationEventMetadata(
        string partitionKey,
        string producer,
        string? correlationId,
        Guid? causationId)
    {
        PartitionKey = partitionKey;
        Producer = producer;
        CorrelationId = correlationId;
        CausationId = causationId;
    }

    /// <summary>
    /// 校验并构造 Metadata；非法输入抛出带稳定原因码的 <see cref="ArgumentException"/>。
    /// </summary>
    public static IntegrationEventMetadata Create(
        string partitionKey,
        string producer,
        string? correlationId = null,
        Guid? causationId = null)
    {
        ValidatePartitionKey(partitionKey);
        ValidateProducer(producer);
        ValidateCorrelationId(correlationId);
        return new IntegrationEventMetadata(
            partitionKey,
            producer,
            correlationId,
            causationId);
    }

    internal static void ValidatePartitionKey(string partitionKey)
    {
        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.PartitionKeyRequired,
                nameof(partitionKey));
        }

        if (Encoding.UTF8.GetByteCount(partitionKey) > MessagingNames.PartitionKeyMaxUtf8Bytes)
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.PartitionKeyTooLong,
                nameof(partitionKey));
        }
    }

    internal static void ValidateProducer(string producer)
    {
        if (string.IsNullOrWhiteSpace(producer)
            || producer.Length > MessagingNames.ProducerMaxLength
            || !MessagingNames.ProducerPattern.IsMatch(producer))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ProducerInvalid,
                nameof(producer));
        }
    }

    internal static void ValidateCorrelationId(string? correlationId)
    {
        if (correlationId is not null
            && correlationId.Length > MessagingNames.CorrelationIdMaxLength)
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.CorrelationIdTooLong,
                nameof(correlationId));
        }
    }
}