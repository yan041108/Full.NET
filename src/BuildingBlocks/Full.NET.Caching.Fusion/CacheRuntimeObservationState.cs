namespace Full.NET.Caching.Fusion;

/// <summary>只表示当前节点是否已接入 SDK 事件观察；不证明条目最新、Redis 健康或其他节点完成失效。</summary>
public sealed class CacheRuntimeObservationState
{
    private int _monitoring;

    /// <summary>当前节点实际监控生命周期状态，observing 表示已订阅事件，unknown 表示没有有效观察。</summary>
    public string Status => Volatile.Read(ref _monitoring) == 1 ? "observing" : "unknown";

    internal void SetMonitoring(bool monitoring) => Volatile.Write(ref _monitoring, monitoring ? 1 : 0);
}
