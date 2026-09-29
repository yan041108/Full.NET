namespace Full.NET.Hosting.Observability;

/// <summary>提供给可选宿主日志出口的已脱敏、预格式化快照。</summary>
/// <remarks>数据由宿主管道构造；外部出口不得修改或长期复用底层缓冲，除非自身预算覆盖投递终态。</remarks>
public sealed class HostLogSnapshot
{
    internal HostLogSnapshot(ReadOnlyMemory<byte> utf8Json, string logEventId, bool isHighPriority)
    {
        Utf8Json = utf8Json;
        LogEventId = logEventId;
        IsHighPriority = isHighPriority;
    }

    /// <summary>不含 B1 受限详情的 Compact JSON UTF-8 字节。</summary>
    public ReadOnlyMemory<byte> Utf8Json { get; }

    /// <summary>同时存在于 JSON 内、由宿主管道生成的事件 ID。</summary>
    public string LogEventId { get; }

    /// <summary>错误及更高级别走独立优先通道。</summary>
    public bool IsHighPriority { get; }
}
