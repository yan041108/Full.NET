namespace Full.NET.Hosting.Observability;

/// <summary>
/// 指定宿主日志的唯一投递入口；未指定时使用 Collector 控制台快照。
/// </summary>
public enum LoggingDeliveryMode
{
    /// <summary>应用内使用可配置的文件和 Console；部署平台还须排除该日志流的远程采集。</summary>
    Local,

    /// <summary>应用内保留 Console；平台采集入口须另行部署并验证。</summary>
    Collector,

    /// <summary>由应用后台适配器投递到日志专用 Kafka。</summary>
    ApplicationKafka,
}
