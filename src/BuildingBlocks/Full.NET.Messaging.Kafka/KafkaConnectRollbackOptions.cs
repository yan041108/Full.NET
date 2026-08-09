namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 回退控制面配置：仅在运维显式启用且完成 Connector/Topic 映射后替换失败关闭实现。
/// </summary>
public sealed class KafkaConnectRollbackOptions
{
    public const string SectionName = "Messaging:KafkaConnectRollback";

    public bool Enabled { get; set; }

    public string? ConnectBaseUri { get; set; }

    public int PrepareTimeoutSeconds { get; set; } = 120;

    public int DrainTimeoutSeconds { get; set; } = 120;

    public int DrainPollIntervalMilliseconds { get; set; } = 1_000;

    public KafkaConnectRollbackStreamBinding[] Streams { get; set; } = [];
}

/// <summary>事件流与 Kafka Connect / Consumer 资源的稳定绑定。</summary>
public sealed class KafkaConnectRollbackStreamBinding
{
    public string EventType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string ConnectorName { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;

    public string ConsumerGroupId { get; set; } = string.Empty;
}
