using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;

namespace Full.NET.Host.Worker;

/// <summary>
/// 记录 Outbox 待处理、到期重试、活动租约、死信、Legacy 空轮询退避与 commit-to-capture 的低基数指标。
/// </summary>
/// <remarks>
/// 指标标签仅允许稳定低基数维度（当前大多数 Gauge 无标签；commit-to-capture 仅允许
/// <c>database_provider</c>）。禁止 Secret、Payload、原始 SQL、TenantId、UserId 或异常文本。
/// Prometheus/OTLP 导出名应与仪表名对齐，例如
/// <c>fullnet.outbox.backlog.oldest_age</c>（单位 s）→ <c>fullnet_outbox_backlog_oldest_age_seconds</c>。
/// </remarks>
internal static class OutboxBacklogTelemetry
{
    public const string MeterName = "Full.NET.Outbox";

    /// <summary>允许出现在本 Meter 标签键中的白名单；用于契约测试。</summary>
    public static readonly IReadOnlyList<string> AllowedTagKeys =
    [
        "database_provider",
    ];

    private static readonly Meter Meter = new(MeterName);
    private static readonly Gauge<long> PendingMessages =
        Meter.CreateGauge<long>(
            "fullnet.outbox.backlog.messages",
            unit: "{message}");
    private static readonly Gauge<double> OldestMessageAge =
        Meter.CreateGauge<double>(
            "fullnet.outbox.backlog.oldest_age",
            unit: "s");
    private static readonly Gauge<long> DueRetryMessages =
        Meter.CreateGauge<long>(
            "fullnet.outbox.retry.due",
            unit: "{message}");
    private static readonly Gauge<long> ActiveLeaseMessages =
        Meter.CreateGauge<long>(
            "fullnet.outbox.lease.active",
            unit: "{message}");
    private static readonly Gauge<long> DeadLetterMessages =
        Meter.CreateGauge<long>(
            "fullnet.outbox.dead_letter.messages",
            unit: "{message}");
    private static readonly Gauge<double> OldestDeadLetterAge =
        Meter.CreateGauge<double>(
            "fullnet.outbox.dead_letter.oldest_age",
            unit: "s");
    private static readonly Gauge<double> EmptyPollBackoff =
        Meter.CreateGauge<double>(
            "fullnet.outbox.legacy.empty_poll.backoff",
            unit: "s");
    private static readonly Histogram<double> CommitToCapture =
        Meter.CreateHistogram<double>(
            "fullnet.outbox.commit_to_capture",
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
            DueRetryMessages.Record(snapshot.DueRetryCount);
            ActiveLeaseMessages.Record(snapshot.ActiveLeaseCount);
            DeadLetterMessages.Record(snapshot.DeadLetterCount);
            var deadLetterAge =
                snapshot.OldestDeadLetteredAtUtc is { } oldestDeadLetter
                    ? Math.Max(
                        0d,
                        (observedAtUtc - oldestDeadLetter).TotalSeconds)
                    : 0d;
            OldestDeadLetterAge.Record(deadLetterAge);
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得阻断 Outbox 的租约领取与消息处理。
        }
    }

    /// <summary>
    /// 记录 Legacy 空轮询当前退避秒数；有工作或满批次时应记录 0，表示退避已复位。
    /// </summary>
    public static void RecordEmptyPollBackoff(TimeSpan delay)
    {
        try
        {
            EmptyPollBackoff.Record(Math.Max(0d, delay.TotalSeconds));
        }
        catch (Exception)
        {
            // 空轮询退避指标失败不得改变轮询节奏本身。
        }
    }

    /// <summary>
    /// 记录 Outbox 提交到 CDC 捕获（或影子 Topic 首次可见）的延迟。
    /// </summary>
    /// <param name="seconds">非负延迟秒数。</param>
    /// <param name="databaseProvider">稳定提供程序码，例如 <c>sqlserver</c> / <c>mysql</c>。</param>
    public static void RecordCommitToCapture(
        double seconds,
        string databaseProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseProvider);
        if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        try
        {
            CommitToCapture.Record(
                seconds,
                new TagList
                {
                    { "database_provider", databaseProvider },
                });
        }
        catch (Exception)
        {
            // commit-to-capture 属于旁路观测，不得影响影子比对或正式消费。
        }
    }
}
