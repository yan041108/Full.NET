using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;

namespace Full.NET.Host.Worker;

internal static class OutboxRetentionTelemetry
{
    public const string MeterName = "Full.NET.Outbox.Retention";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DeletedRows = Meter.CreateCounter<long>(
        "fullnet.outbox.retention.deleted_rows",
        unit: "{row}");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "fullnet.outbox.retention.failures",
        unit: "{failure}");
    private static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>(
            "fullnet.outbox.retention.run_duration",
            unit: "s");
    private static readonly Gauge<long> LastSuccessTime =
        Meter.CreateGauge<long>(
            "fullnet.outbox.retention.last_success_time",
            unit: "s");

    internal static void RecordSuccess(
        OutboxRetentionResult result,
        DatabaseProvider provider,
        TimeSpan duration,
        DateTimeOffset completedAtUtc)
    {
        try
        {
            var providerTag = CreateProviderTag(provider);
            DeletedRows.Add(
                result.DeletedRows,
                providerTag,
                new KeyValuePair<string, object?>("result", "success"));
            RunDuration.Record(
                duration.TotalSeconds,
                providerTag,
                new KeyValuePair<string, object?>("result", "success"));
            LastSuccessTime.Record(
                completedAtUtc.ToUnixTimeSeconds(),
                providerTag);
        }
        catch (Exception)
        {
            // 遥测是旁路能力，指标消费者故障不得改变清理事务和轮询存活性。
        }
    }

    internal static void RecordFailure(
        DatabaseProvider provider,
        TimeSpan duration)
    {
        try
        {
            var providerTag = CreateProviderTag(provider);
            Failures.Add(
                1,
                providerTag,
                new KeyValuePair<string, object?>("result", "failure"));
            RunDuration.Record(
                duration.TotalSeconds,
                providerTag,
                new KeyValuePair<string, object?>("result", "failure"));
        }
        catch (Exception)
        {
            // 失败指标自身异常不得终止后续轮询。
        }
    }

    private static KeyValuePair<string, object?> CreateProviderTag(
        DatabaseProvider provider) =>
        new(
            "provider",
            provider == DatabaseProvider.SqlServer ? "sql_server" : "my_sql");
}
