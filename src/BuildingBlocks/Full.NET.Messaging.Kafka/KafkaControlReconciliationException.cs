namespace Full.NET.Messaging.Kafka;

/// <summary>控制纠偏的低基数诊断原因；不携带端点、SQL、凭据或原始异常消息。</summary>
internal sealed class KafkaControlReconciliationException(string reasonCode) : InvalidOperationException(reasonCode)
{
    internal string ReasonCode { get; } = reasonCode;
}
