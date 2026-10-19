namespace Full.NET.Hosting.Observability;

/// <summary>宿主启动时固定的入口配置，不代表外部采集、Broker ACK 或 ES 写入成功。</summary>
public interface ILoggingDeliverySelection
{
    /// <summary>实际选用的 Local、Collector 或 ApplicationKafka 入口。</summary>
    LoggingDeliveryMode Mode { get; }
}

/// <summary>启动快照，管理面不能以热变配置冒充已构建出口。</summary>
internal sealed record LoggingDeliverySelection(LoggingDeliveryMode Mode) : ILoggingDeliverySelection;
