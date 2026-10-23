using System.Diagnostics.Metrics;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>投递积压与尝试结果的低基数指标；禁止 Profile/租户/用户/外部 Id 标签。</summary>
internal static class NotificationDeliveryTelemetry
{
    public const string MeterName = "Full.NET.Notifications.Delivery";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Attempts =
        Meter.CreateCounter<long>("fullnet.notifications.delivery.attempts", unit: "{attempt}");
    private static readonly Histogram<double> AttemptDuration =
        Meter.CreateHistogram<double>("fullnet.notifications.delivery.attempt.duration", unit: "ms");
    private static readonly Gauge<long> Backlog =
        Meter.CreateGauge<long>("fullnet.notifications.delivery.backlog", unit: "{delivery}");
    private static readonly Gauge<double> OldestAge =
        Meter.CreateGauge<double>("fullnet.notifications.delivery.oldest_age", unit: "s");
    private static readonly Gauge<long> AwaitingReconciliation =
        Meter.CreateGauge<long>("fullnet.notifications.delivery.awaiting_reconciliation", unit: "{delivery}");
    private static readonly Counter<long> QuotaRejected =
        Meter.CreateCounter<long>("fullnet.notifications.delivery.quota_rejected", unit: "{delivery}");
    private static readonly Gauge<long> DeadLetters =
        Meter.CreateGauge<long>("fullnet.notifications.delivery.dead_lettered", unit: "{delivery}");

    /// <summary>存量死信单独采样，避免用已清零的重试队列掩盖需要人工处理的失败。</summary>
    public static void RecordDeadLetters(long count) => Record(() => DeadLetters.Record(count));

    /// <summary>记录因配额阻塞或缺失配置而未外发的次数，reason 取闭合的失败关闭类别。</summary>
    public static void RecordQuotaRejected(string reason) =>
        Record(() => QuotaRejected.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason)));

    public static void RecordAttempt(
        string providerTypeKey,
        string channelKey,
        string resultCategory,
        double durationMilliseconds) =>
        Record(() =>
        {
            var provider = new KeyValuePair<string, object?>("provider_type", providerTypeKey);
            var channel = new KeyValuePair<string, object?>("channel", channelKey);
            var result = new KeyValuePair<string, object?>("result_category", resultCategory);
            Attempts.Add(1, provider, channel, result);
            AttemptDuration.Record(durationMilliseconds, provider, channel, result);
        });

    public static void RecordBacklog(long count, double oldestAgeSeconds) =>
        Record(() =>
        {
            Backlog.Record(count);
            OldestAge.Record(Math.Max(0d, oldestAgeSeconds));
        });

    /// <summary>记录等待回执对账或人工处置的暂挂 unknown 数量，与可自动执行的积压分开告警。</summary>
    public static void RecordAwaitingReconciliation(long count) =>
        Record(() => AwaitingReconciliation.Record(count));

    private static void Record(Action record)
    {
        try
        {
            record();
        }
        catch (Exception)
        {
            // 指标是观测旁路，不得反转已经提交的投递状态。
        }
    }
}
