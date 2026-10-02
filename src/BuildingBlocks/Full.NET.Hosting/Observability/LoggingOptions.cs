namespace Full.NET.Hosting.Observability;

/// <summary>
/// 定义普通与高优先级日志通道的有界容量。
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "FullNet:Logging";

    /// <summary>
    /// 显式投递入口；为空时保留旧 Console/Elasticsearch 配置的兼容行为。
    /// </summary>
    public LoggingDeliveryMode? DeliveryMode { get; set; }

    /// <summary>
    /// 部署清单注入的预期入口；与显式模式不一致时拒绝启动，不代表已验证平台采集器。
    /// </summary>
    public LoggingDeliveryMode? ExpectedDeliveryMode { get; set; }

    /// <summary>
    /// 获取或设置普通日志通道容量。
    /// </summary>
    public int AsyncBufferSize { get; set; } = 10_000;

    /// <summary>
    /// 获取或设置 Error/Critical 高优先级日志通道容量。
    /// </summary>
    public int HighPriorityAsyncBufferSize { get; set; } = 1_000;

    /// <summary>
    /// 单条 Compact JSON 快照允许的最大 UTF-8 字节数。
    /// </summary>
    public int MaxEventBytes { get; set; } = 16_384;

    /// <summary>显式索引路由版本；与 IndexRetentionDays 成对配置，零表示尚未启用索引路由。</summary>
    public int IndexRouteVersion { get; set; }

    /// <summary>自事件发生 UTC 时间起计算的固定保留天数；与 IndexRouteVersion 成对配置。</summary>
    public int IndexRetentionDays { get; set; }

    /// <summary>
    /// 普通日志通道待消费和 Sink 在途快照共享的字节预算。
    /// </summary>
    public long GeneralQueueMaxBytes { get; set; } = 67_108_864;

    /// <summary>
    /// 高优先级通道独立的待消费和 Sink 在途快照字节预算。
    /// </summary>
    public long HighPriorityQueueMaxBytes { get; set; } = 8_388_608;

    /// <summary>
    /// 获取或设置是否在普通日志队列满时阻塞调用方。
    /// </summary>
    /// <remarks>
    /// Full.NET 禁止启用该兼容配置；属性仅用于在启动时给出明确校验错误。
    /// </remarks>
    public bool BlockWhenFull { get; set; }

    /// <summary>
    /// 获取或设置两条日志通道在宿主退出时共享的最大排空时间。
    /// </summary>
    /// <remarks>
    /// 该预算只约束进程退出等待，不允许日志调用方在运行期间同步等待 Sink。
    /// </remarks>
    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
