using System.Diagnostics.Metrics;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>记录任务终态与重试排期的低基数运行指标。</summary>
internal static class JobsTelemetry
{
    public const string MeterName = "Full.NET.Jobs";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ExecutionTransitions =
        Meter.CreateCounter<long>(
            "fullnet.jobs.execution.transitions",
            unit: "{execution}");
    private static readonly Counter<long> RetryScheduled =
        Meter.CreateCounter<long>(
            "fullnet.jobs.retry.scheduled",
            unit: "{execution}");
    private static readonly Histogram<double> RetryDelay =
        Meter.CreateHistogram<double>(
            "fullnet.jobs.retry.delay",
            unit: "s");
    private static readonly Counter<long> RetryExhausted =
        Meter.CreateCounter<long>(
            "fullnet.jobs.retry.exhausted",
            unit: "{execution}");
    private static readonly Gauge<long> BacklogExecutions =
        Meter.CreateGauge<long>(
            "fullnet.jobs.backlog.executions",
            unit: "{execution}");
    private static readonly Gauge<double> OldestBacklogAge =
        Meter.CreateGauge<double>(
            "fullnet.jobs.backlog.oldest_age",
            unit: "s");
    private static readonly Gauge<long> DueRetryExecutions =
        Meter.CreateGauge<long>(
            "fullnet.jobs.retry.due",
            unit: "{execution}");
    private static readonly Gauge<double> OldestDueRetryAge =
        Meter.CreateGauge<double>(
            "fullnet.jobs.retry.oldest_due_age",
            unit: "s");

    public static void RecordSucceeded() =>
        Record(() => RecordTransition("succeeded"));

    public static void RecordFailed(bool retryExhausted) =>
        Record(() =>
        {
            RecordTransition("failed");
            if (retryExhausted)
            {
                RetryExhausted.Add(1);
            }
        });

    public static void RecordRetryScheduled(double delaySeconds) =>
        Record(() =>
        {
            RecordTransition("retry_scheduled");
            RetryScheduled.Add(1);
            RetryDelay.Record(delaySeconds);
        });

    public static void RecordBacklog(
        JobsBacklogSnapshot snapshot,
        DateTimeOffset observedAtUtc) =>
        Record(() =>
        {
            BacklogExecutions.Record(snapshot.PendingCount);
            OldestBacklogAge.Record(AgeSeconds(
                observedAtUtc,
                snapshot.OldestClaimableCreatedAtUtc));
            DueRetryExecutions.Record(snapshot.DueRetryCount);
            OldestDueRetryAge.Record(AgeSeconds(
                observedAtUtc,
                snapshot.OldestDueRetryAtUtc));
        });

    private static void RecordTransition(string outcome) =>
        ExecutionTransitions.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome));

    private static double AgeSeconds(
        DateTimeOffset observedAtUtc,
        DateTimeOffset? startedAtUtc) =>
        startedAtUtc is { } started
            ? Math.Max(0d, (observedAtUtc - started).TotalSeconds)
            : 0d;

    private static void Record(Action record)
    {
        try
        {
            record();
        }
        catch (Exception)
        {
            // 指标属于观测旁路；监听器故障不得反转已经提交的任务状态。
        }
    }
}
