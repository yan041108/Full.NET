namespace Full.NET.Hosting.Observability;

/// <summary>
/// 宿主启动时确定的日志入口配置事实；不代表平台或远端已完成投递。
/// </summary>
public interface ILoggingDeliverySelection
{
    /// <summary>显式入口；为空时沿用旧版配置。</summary>
    LoggingDeliveryMode? Mode { get; }

    /// <summary>启动时旧 Elasticsearch Sink 是否声明启用。</summary>
    bool LegacyElasticsearchEnabled { get; }
}

/// <summary>固定启动快照，避免管理面随配置热变而误报实际 Sink 选择。</summary>
internal sealed record LoggingDeliverySelection(
    LoggingDeliveryMode? Mode,
    bool LegacyElasticsearchEnabled) : ILoggingDeliverySelection;
