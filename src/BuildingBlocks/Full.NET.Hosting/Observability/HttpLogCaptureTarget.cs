namespace Full.NET.Hosting.Observability;

/// <summary>限定 HTTP 投影可写入的目的地；Restricted 详情在独立 B1 资格接入前始终关闭。</summary>
public enum HttpLogCaptureTarget
{
    /// <summary>可进入普通日志管道的 Internal 请求摘要。</summary>
    B2InternalSummary = 0,

    /// <summary>仅供 B1 独立详情存储的 Restricted 投影。</summary>
    B1RestrictedDetail = 1,
}
