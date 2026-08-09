namespace Full.NET.Host.Worker;

/// <summary>
/// Worker 消息交付显式模式；生产默认保持 <see cref="LegacyPolling"/> 直至切流门禁通过。
/// </summary>
public enum MessagingWorkerMode
{
    /// <summary>旧 Outbox 轮询 Worker 为唯一正式交付路径。</summary>
    LegacyPolling = 0,

    /// <summary>CDC 影子 Topic 比对；不得注册 Kafka 正式业务订阅。</summary>
    ShadowCdc = 1,

    /// <summary>
    /// CDC Relay + Kafka 正式 Consumer 与 Legacy Poller 并存。
    /// 所有权为 CdcKafka 的事件流走 Kafka Consumer，其余流继续走 Legacy Poller。
    /// CdcKafka 枚举值作为本模式的一个发布周期内过时别名保留。
    /// </summary>
    HybridKafka = 2,

    /// <summary>
    /// 已过时。使用 <see cref="HybridKafka"/> 代替。
    /// 保留一版以便旧配置平滑迁移；启动时映射到 HybridKafka 语义。
    /// </summary>
    [Obsolete("Use HybridKafka instead. CdcKafka is retained as a one-release alias.")]
    CdcKafka = 2,
}
