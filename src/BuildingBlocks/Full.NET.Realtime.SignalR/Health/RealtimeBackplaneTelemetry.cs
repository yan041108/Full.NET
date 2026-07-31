using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.Realtime.SignalR.Health;

/// <summary>记录 Realtime Redis ready 探针的低基数状态、结果与耗时。</summary>
internal static class RealtimeBackplaneTelemetry
{
    public const string MeterName = "fullnet.realtime";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Gauge<long> ReadinessState =
        Meter.CreateGauge<long>(
            "fullnet.realtime.backplane.readiness.state",
            unit: "{state}");
    private static readonly Counter<long> ReadinessChecks =
        Meter.CreateCounter<long>(
            "fullnet.realtime.backplane.readiness.checks",
            unit: "{check}");
    private static readonly Histogram<double> ReadinessDuration =
        Meter.CreateHistogram<double>(
            "fullnet.realtime.backplane.readiness.duration",
            unit: "ms");

    public static void Record(
        long startedTimestamp,
        string outcome,
        bool isReady)
    {
        try
        {
            var tags = new TagList
            {
                { "outcome", outcome },
            };
            ReadinessState.Record(isReady ? 1 : 0);
            ReadinessChecks.Add(1, tags);
            ReadinessDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds,
                tags);
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得改变 ready 结果或覆盖 Redis 探针异常。
        }
    }
}
