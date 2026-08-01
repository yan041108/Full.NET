using System.Diagnostics.Metrics;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Retention;

internal static class AuditingRetentionTelemetry
{
    public const string MeterName = "Full.NET.Auditing.Retention";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DeletedRows = Meter.CreateCounter<long>(
        "fullnet.auditing.retention.deleted_rows",
        unit: "{row}");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "fullnet.auditing.retention.failures",
        unit: "{failure}");
    private static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>(
            "fullnet.auditing.retention.run_duration",
            unit: "s");
    private static readonly Gauge<long> LastSuccessTime =
        Meter.CreateGauge<long>(
            "fullnet.auditing.retention.last_success_time",
            unit: "s");

    internal static void RecordSuccess(
        AuditingRetentionResult result,
        DatabaseProvider provider,
        TimeSpan duration,
        DateTimeOffset completedAtUtc)
    {
        try
        {
            RecordDeletedRows(
                result.AccessDeleted,
                "access",
                provider);
            RecordDeletedRows(
                result.OperationDeleted,
                "operation",
                provider);
            RecordDeletedRows(
                result.ExceptionDeleted,
                "exception",
                provider);
            RecordDeletedRows(
                result.OutboundDeleted,
                "outbound",
                provider);
            RunDuration.Record(
                duration.TotalSeconds,
                CreateProviderTag(provider),
                new KeyValuePair<string, object?>("result", "success"));
            LastSuccessTime.Record(
                completedAtUtc.ToUnixTimeSeconds(),
                CreateProviderTag(provider));
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
            // 遥测是旁路能力，失败计数自身异常不得终止后续轮询。
        }
    }

    private static void RecordDeletedRows(
        int count,
        string category,
        DatabaseProvider provider) =>
        DeletedRows.Add(
            count,
            new KeyValuePair<string, object?>("category", category),
            CreateProviderTag(provider),
            new KeyValuePair<string, object?>("result", "success"));

    private static KeyValuePair<string, object?> CreateProviderTag(
        DatabaseProvider provider) =>
        new(
            "provider",
            provider == DatabaseProvider.SqlServer ? "sql_server" : "my_sql");
}
