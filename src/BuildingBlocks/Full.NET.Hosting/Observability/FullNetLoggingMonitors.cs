namespace Full.NET.Hosting.Observability;

/// <summary>
/// 持有普通与高优先级日志通道的独立运行时监控器。
/// </summary>
public sealed class FullNetLoggingMonitors : IDisposable
{
    /// <summary>
    /// 获取普通日志通道监控器。
    /// </summary>
    public FullNetAsyncLogMonitor General { get; } =
        new(FullNetAsyncLogMonitor.GeneralChannel);

    /// <summary>
    /// 获取 Error/Critical 高优先级日志通道监控器。
    /// </summary>
    public FullNetAsyncLogMonitor HighPriority { get; } =
        new(FullNetAsyncLogMonitor.HighPriorityChannel);

    /// <inheritdoc />
    public void Dispose()
    {
        General.Dispose();
        HighPriority.Dispose();
    }
}
