using Full.NET.Benchmarks.MixedLoad;

namespace Full.NET.Benchmarks.Logging;

/// <summary>请求延迟样本必须同时满足响应与日志交付对账，避免以丢日志换取低延迟。</summary>
public static class LoggingRequestEvidence
{
    /// <summary>固定节奏的目标发送时点；不将客户端等待混入 HTTP 响应延迟。</summary>
    public static double ScheduledMilliseconds(int sequence, int requestsPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfLessThan(requestsPerSecond, 1);
        return sequence * 1000d / requestsPerSecond;
    }

    /// <summary>持续档必须达到规定发送跨度；调度延迟另外保留，不能隐藏过载。</summary>
    public static void ValidateSustainedWindow(int requests, int requestsPerSecond, double elapsedMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requests, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requests, 10000);
        var minimum = ScheduledMilliseconds(requests - 1, requestsPerSecond);
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < minimum || elapsedMilliseconds > 30000)
            throw new InvalidOperationException("持续请求发送窗口不足或超过 30 秒预算。");
    }

    /// <summary>验证有限延迟、预期日志/投影和给定丢弃计数为零后计算实际分位数。</summary>
    public static MixedLoadLatencyStatistics Calculate(double[] latencies, int unexpectedResponses,
        int expectedEvents, int observedEvents, long dropped, int expectedProjections, int observedProjections)
    {
        ArgumentNullException.ThrowIfNull(latencies);
        if (latencies.Length is < 1 or > 10000 || latencies.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("延迟样本必须有限、非负且不超过一万条。", nameof(latencies));
        if (unexpectedResponses != 0 || dropped != 0 || expectedEvents < 0 || expectedEvents != observedEvents
            || expectedProjections < 0 || expectedProjections != observedProjections)
            throw new InvalidOperationException("请求响应、日志或投影对账未通过。");
        return MixedLoadLatencyStatistics.Calculate(latencies);
    }
}
