namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 正式事件流的单一发布与消费所有权；同一 (EventType, SchemaVersion) 在目录中只能声明一个所有者。
/// </summary>
public enum EventDeliveryOwner
{
    /// <summary>旧 Outbox 轮询 Worker 负责发布与消费。</summary>
    LegacyPolling = 0,

    /// <summary>CDC 影子 Topic 仅用于比对，不得绑定业务消费者。</summary>
    ShadowCdc = 1,

    /// <summary>CDC Relay + Kafka 正式业务交付路径。</summary>
    CdcKafka = 2,
}