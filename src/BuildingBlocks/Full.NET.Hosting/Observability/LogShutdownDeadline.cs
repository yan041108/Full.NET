using System.Diagnostics;

namespace Full.NET.Hosting.Observability;

/// <summary>整个宿主日志退出过程共用的单调截止时间；不能约束不支持取消的 native 释放。</summary>
public sealed class LogShutdownDeadline
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly TimeSpan _budget;

    /// <summary>在停止入口前创建截止时间；零表示不再等待排空。</summary>
    public LogShutdownDeadline(TimeSpan budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
        _budget = budget;
    }

    /// <summary>所有通道每次操作前重新读取剩余预算，不能重新启动完整超时。</summary>
    public TimeSpan Remaining => _budget - Stopwatch.GetElapsedTime(_started) is var remaining && remaining > TimeSpan.Zero
        ? remaining : TimeSpan.Zero;
}
