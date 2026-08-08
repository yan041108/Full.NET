namespace Full.NET.Host.Worker;

/// <summary>
/// Worker 消息交付显式模式；生产默认保持 <see cref="LegacyPolling"/> 直至 Task 11 切流门禁通过。
/// </summary>
public enum MessagingWorkerMode
{
    /// <summary>旧 Outbox 轮询 Worker 为唯一正式交付路径。</summary>
    LegacyPolling = 0,

    /// <summary>CDC 影子 Topic 比对；不得注册 Kafka 正式业务订阅。</summary>
    ShadowCdc = 1,

    /// <summary>CDC Relay + Kafka 正式 Consumer；关闭旧轮询发布路径。</summary>
    CdcKafka = 2,
}
