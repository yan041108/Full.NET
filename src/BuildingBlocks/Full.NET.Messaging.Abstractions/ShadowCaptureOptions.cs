namespace Full.NET.Messaging.Abstractions;

/// <summary>受控业务影子采集策略；仅允许单个已登记试点流，默认不影响业务写入。</summary>
public sealed class ShadowCaptureOptions
{
    /// <summary>跨 API/Worker 共用的配置节。</summary>
    public const string SectionName = "Messaging:ShadowCapture";
    /// <summary>是否允许创建采集窗口及在 Legacy 写入时镜像真实消息；默认关闭。</summary>
    public bool Enabled { get; set; }
    /// <summary>允许采集的服务端事件类型；不能由业务请求指定其他流。</summary>
    public string EventType { get; set; } = string.Empty;
    /// <summary>允许采集的事件版本。</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>每代采集消息数量上限，允许 1～10000。</summary>
    public int MaximumEvents { get; set; } = 1000;
    /// <summary>每代真实载荷字节总预算，最多 32 MiB；不包含重复序列化。</summary>
    public long MaximumPayloadBytes { get; set; } = 33_554_432;
    /// <summary>采集窗口最大秒数，允许 1～300；期限不能随写入续期。</summary>
    public int MaximumWindowSeconds { get; set; } = 300;
}
