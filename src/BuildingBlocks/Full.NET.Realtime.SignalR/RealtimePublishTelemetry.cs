using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Realtime.SignalR.Health;

namespace Full.NET.Realtime.SignalR;

/// <summary>记录 Realtime 发布结果与耗时的低基数指标。</summary>
internal static class RealtimePublishTelemetry
{
    private static readonly Meter Meter =
        new(RealtimeBackplaneTelemetry.MeterName);
    private static readonly Counter<long> PublishAttempts =
        Meter.CreateCounter<long>(
            "fullnet.realtime.publish.attempts",
            unit: "{attempt}");
    private static readonly Histogram<double> PublishDuration =
        Meter.CreateHistogram<double>(
            "fullnet.realtime.publish.duration",
            unit: "ms");

    public static void Record(
        long startedTimestamp,
        string target,
        string outcome)
    {
        try
        {
            var tags = new TagList
            {
                { "target", target },
                { "outcome", outcome },
            };
            PublishAttempts.Add(1, tags);
            PublishDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds,
                tags);
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得改变 SignalR 发送结果或覆盖原始异常。
        }
    }
}
