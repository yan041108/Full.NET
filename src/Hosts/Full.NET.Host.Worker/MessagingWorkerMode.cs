namespace Full.NET.Host.Worker;

/// <summary>
/// Worker 消息交付显式模式；生产默认保持 <see cref="LegacyPolling"/> 直至切流门禁通过。
/// </summary>
/// <remarks>枚举成员数值发布后不可调整；新增成员只能追加到末尾。</remarks>
public enum MessagingWorkerMode
{
    /// <summary>旧 Outbox 轮询 Worker 为唯一正式交付路径。</summary>
    LegacyPolling = 0,

    /// <summary>CDC 影子 Topic 比对；不得注册 Kafka 正式业务订阅。</summary>
    ShadowCdc = 1,

    /// <summary>
    /// CDC Relay + Kafka 正式 Consumer 与 Legacy Poller 并存。
    /// 所有权为 CdcKafka 的事件流走 Kafka Consumer，其余流继续走 Legacy Poller。
    /// </summary>
    HybridKafka = 2,

}
