namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 回退控制面配置：仅在运维显式启用且完成 Connector/Topic 映射后替换失败关闭实现。
/// </summary>
public sealed class KafkaConnectRollbackOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Messaging:KafkaConnectRollback";

    /// <summary>
    /// 是否启用 Kafka Connect 回退控制面；默认 false，仅在运维显式启用且完成 Connector/Topic 映射后生效。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Kafka Connect REST API 基地址，例如 http://connect:8083。</summary>
    public string? ConnectBaseUri { get; set; }

    /// <summary>准备阶段（Connector 暂停/复位）超时秒数；默认 120。</summary>
    public int PrepareTimeoutSeconds { get; set; } = 120;

    /// <summary>排空阶段（等待在途消息消费完毕）超时秒数；默认 120。</summary>
    public int DrainTimeoutSeconds { get; set; } = 120;

    /// <summary>排空阶段轮询 Connector 状态的间隔毫秒数；默认 1000。</summary>
    public int DrainPollIntervalMilliseconds { get; set; } = 1_000;

    /// <summary>事件流与 Kafka Connect / Consumer 资源的稳定绑定列表。</summary>
    public KafkaConnectRollbackStreamBinding[] Streams { get; set; } = [];
}

/// <summary>事件流与 Kafka Connect / Consumer 资源的稳定绑定。</summary>
public sealed class KafkaConnectRollbackStreamBinding
{
    /// <summary>事件类型名称，对应领域事件的契约类型标识。</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>事件契约 Schema 版本号。</summary>
    public int SchemaVersion { get; set; }

    /// <summary>对应 Kafka Connect Connector 的名称。</summary>
    public string ConnectorName { get; set; } = string.Empty;

    /// <summary>对应 Kafka Topic 名称。</summary>
    public string TopicName { get; set; } = string.Empty;

    /// <summary>对应 Consumer Group 标识。</summary>
    public string ConsumerGroupId { get; set; } = string.Empty;
}
