namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 集成事件处理失败分类，对应规格 §8 的稳定处置语义。
/// </summary>
public enum IntegrationEventFailureKind
{
    Contract = 0,
    Security = 1,
    Transient = 2,
    Business = 3,
    Capacity = 4,
}

/// <summary>
/// 集成事件失败稳定原因码前缀。
/// </summary>
public static class IntegrationEventFailureCodes
{
    public const string ContractPrefix = "messaging.contract.";

    public const string SecurityPrefix = "messaging.security.";

    public const string TransientPrefix = "messaging.transient.";

    public const string BusinessPrefix = "messaging.business.";

    public const string CapacityPrefix = "messaging.capacity.";

    public const string PartitionKeyRequired = ContractPrefix + "partition_key_required";

    public const string PartitionKeyTooLong = ContractPrefix + "partition_key_too_long";

    public const string SchemaVersionInvalid = ContractPrefix + "schema_version_invalid";

    public const string MessageTypeInvalid = ContractPrefix + "message_type_invalid";

    public const string ContentTypeInvalid = ContractPrefix + "content_type_invalid";

    public const string ProducerInvalid = ContractPrefix + "producer_invalid";

    public const string CorrelationIdTooLong = ContractPrefix + "correlation_id_too_long";

    public const string TraceParentInvalid = ContractPrefix + "trace_parent_invalid";

    public const string PayloadRequired = ContractPrefix + "payload_required";

    public const string MessageIdPayloadMismatch = ContractPrefix + "message_id_payload_mismatch";

    public const string TopicCodeInvalid = ContractPrefix + "topic_code_invalid";

    public const string ConsumerNameInvalid = ContractPrefix + "consumer_name_invalid";

    public const string SchemaVersionUnknown = ContractPrefix + "schema_version_unknown";
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