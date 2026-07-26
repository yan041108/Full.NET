using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;

namespace Full.NET.Host.Worker;

/// <summary>
/// 记录 Outbox 积压数量与最老消息年龄的低基数指标。
/// </summary>
internal static class OutboxBacklogTelemetry
{
    public const string MeterName = "Full.NET.Outbox";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Gauge<long> PendingMessages =
        Meter.CreateGauge<long>(
            "fullnet.outbox.backlog.messages",
            unit: "{message}");
    private static readonly Gauge<double> OldestMessageAge =
        Meter.CreateGauge<double>(
            "fullnet.outbox.backlog.oldest_age",
            unit: "s");

    public static void Record(
        OutboxBacklogSnapshot snapshot,
        DateTimeOffset observedAtUtc)
    {
        try
        {
            PendingMessages.Record(snapshot.PendingCount);
            var age = snapshot.OldestOccurredAtUtc is { } oldest
                ? Math.Max(0d, (observedAtUtc - oldest).TotalSeconds)
                : 0d;
            OldestMessageAge.Record(age);
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得阻断 Outbox 的租约领取与消息处理。
        }
    }
}
