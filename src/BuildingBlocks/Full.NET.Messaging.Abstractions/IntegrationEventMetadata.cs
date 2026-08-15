using System.Text;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Outbox 写入时由业务模块显式提供的分区、生产者与关联元数据。
/// CDC Relay 依赖本对象构造 Kafka Record 头部；切流到 CdcKafka 后仍缺失元数据将导致 Outbox 写入失败关闭，
/// 防止已切流链路回退到 Legacy Polling 表造成重复投递或顺序错乱。
/// </summary>
public sealed class IntegrationEventMetadata
{
    /// <summary>
    /// Kafka 分区键；同一聚合根事件必须使用相同值，以保证该实体的所有变更落在同一分区并按发生顺序消费。
    /// </summary>
    public string PartitionKey { get; }

    /// <summary>
    /// 生产者模块标识，用于投递审计与 DLQ 责任归属；格式需符合 <c>MessagingNames.ProducerPattern</c> 正则。
    /// </summary>
    public string Producer { get; }

    /// <summary>
    /// 跨服务追踪 CorrelationId；若当前请求已有上游链路则直接透传，缺失时可由入口层生成新 Guid。
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// 直接触发本事件的命令/前序事件 ID；空表示本事件是用户交互或定时作业发起的链路起点。
    /// </summary>
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