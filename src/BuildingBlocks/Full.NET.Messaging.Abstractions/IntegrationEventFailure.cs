namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 集成事件处理失败分类，对应规格 §8 的稳定处置语义。
/// 分类直接决定消息是否进入重试队列、是否转投 DLQ 或立即丢弃，禁止运行期动态映射。
/// </summary>
public enum IntegrationEventFailureKind
{
    /// <summary>
    /// 契约类失败：MessageType/SchemaVersion/Payload 等格式不合法，重试无意义，应直接进入 DLQ。
    /// </summary>
    Contract = 0,

    /// <summary>
    /// 安全类失败：鉴权失败、越权访问、签名不正确等；通常直接丢弃并报警，避免攻击者触发重试风暴。
    /// </summary>
    Security = 1,

    /// <summary>
    /// 瞬态类失败：数据库超时、网络闪断、下游限流等；可按 <c>KafkaRetryRouter</c> 退避阶段重投。
    /// </summary>
    Transient = 2,

    /// <summary>
    /// 业务规则类失败：状态机不允许、数据冲突、幂等拒绝等；业务逻辑稳定失败，直接 DLQ 等待人工介入。
    /// </summary>
    Business = 3,

    /// <summary>
    /// 容量类失败：线程池耗尽、连接池满、缓冲压过高；重试间隔应更久并优先启用 Backpressure 暂停拉取。
    /// </summary>
    Capacity = 4,
}

/// <summary>
/// 集成事件失败稳定原因码前缀与常量。
/// 所有原因码由前缀 + 具体失败项构成，用于遥测聚合与 DLQ 检索；禁止使用英文句子或易变描述直接作为编码。
/// </summary>
public static class IntegrationEventFailureCodes
{
    /// <summary>契约类失败原因码前缀。</summary>
    public const string ContractPrefix = "messaging.contract.";

    /// <summary>安全类失败原因码前缀。</summary>
    public const string SecurityPrefix = "messaging.security.";

    /// <summary>瞬态类失败原因码前缀。</summary>
    public const string TransientPrefix = "messaging.transient.";

    /// <summary>业务类失败原因码前缀。</summary>
    public const string BusinessPrefix = "messaging.business.";

    /// <summary>容量类失败原因码前缀。</summary>
    public const string CapacityPrefix = "messaging.capacity.";

    /// <summary>未提供必填分区键。</summary>
    public const string PartitionKeyRequired = ContractPrefix + "partition_key_required";

    /// <summary>分区键 UTF-8 字节长度超过上限。</summary>
    public const string PartitionKeyTooLong = ContractPrefix + "partition_key_too_long";

    /// <summary>SchemaVersion 小于 1 或超出声明范围。</summary>
    public const string SchemaVersionInvalid = ContractPrefix + "schema_version_invalid";

    /// <summary>MessageType 为空或不符合命名正则。</summary>
    public const string MessageTypeInvalid = ContractPrefix + "message_type_invalid";

    /// <summary>ContentType 不是受支持的序列化格式。</summary>
    public const string ContentTypeInvalid = ContractPrefix + "content_type_invalid";

    /// <summary>生产者标识不符合命名规范。</summary>
    public const string ProducerInvalid = ContractPrefix + "producer_invalid";

    /// <summary>CorrelationId 字符串长度超过限制。</summary>
    public const string CorrelationIdTooLong = ContractPrefix + "correlation_id_too_long";

    /// <summary>TraceParent 格式不符合 W3C Trace Context。</summary>
    public const string TraceParentInvalid = ContractPrefix + "trace_parent_invalid";

    /// <summary>事件载荷字节为空或长度为 0。</summary>
    public const string PayloadRequired = ContractPrefix + "payload_required";

    /// <summary>Envelope 外层 EventId 与 Payload 内声明的 MessageId 不一致。</summary>
    public const string MessageIdPayloadMismatch = ContractPrefix + "message_id_payload_mismatch";

    /// <summary>TopicCode 未在目录中注册或格式非法。</summary>
    public const string TopicCodeInvalid = ContractPrefix + "topic_code_invalid";

    /// <summary>消费者组名称不符合正则或长度约束。</summary>
    public const string ConsumerNameInvalid = ContractPrefix + "consumer_name_invalid";

    /// <summary>消费者未订阅该 MessageType + SchemaVersion 组合。</summary>
    public const string SchemaVersionUnknown = ContractPrefix + "schema_version_unknown";

    /// <summary>
    /// CdcKafka 流要求业务显式提供 <see cref="IntegrationEventMetadata"/>，
    /// 以便 CDC Relay 构造 Kafka Record 的分区键、生产者标识与追踪上下文。
    /// 缺失时路由器失败关闭——绝不退化为 Legacy 表写入，防止在已切流的链路下
    /// 产生重复投递或顺序不一致风险。
    /// </summary>
    public const string OutboxEventMetadataMissing =
        ContractPrefix + "outbox_event_metadata_missing";
}

/// <summary>
/// 描述一次可分类、可遥测且不含敏感载荷的失败结果。
/// </summary>
public sealed record IntegrationEventFailure(
    IntegrationEventFailureKind Kind,
    string Code,
    string Summary)
{
    /// <summary>
    /// 根据稳定原因码推断失败分类。
    /// </summary>
    public static IntegrationEventFailureKind ResolveKind(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code.StartsWith(IntegrationEventFailureCodes.ContractPrefix, StringComparison.Ordinal))
        {
            return IntegrationEventFailureKind.Contract;
        }

        if (code.StartsWith(IntegrationEventFailureCodes.SecurityPrefix, StringComparison.Ordinal))
        {
            return IntegrationEventFailureKind.Security;
        }

        if (code.StartsWith(IntegrationEventFailureCodes.TransientPrefix, StringComparison.Ordinal))
        {
            return IntegrationEventFailureKind.Transient;
        }

        if (code.StartsWith(IntegrationEventFailureCodes.BusinessPrefix, StringComparison.Ordinal))
        {
            return IntegrationEventFailureKind.Business;
        }

        if (code.StartsWith(IntegrationEventFailureCodes.CapacityPrefix, StringComparison.Ordinal))
        {
            return IntegrationEventFailureKind.Capacity;
        }

        throw new ArgumentException(
            $"Unsupported integration event failure code prefix: {code}",
            nameof(code));
    }
}