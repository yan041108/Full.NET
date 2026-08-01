using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>B1 微批低基数指标；禁止 TraceId/UserId/TenantId 等高基数标签。</summary>
internal static class AuditMicroBatchTelemetry
{
    public const string MeterName = "Full.NET.Auditing.MicroBatch";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Accepted =
        Meter.CreateCounter<long>("fullnet.auditing.microbatch.accepted");
    private static readonly Counter<long> Rejected =
        Meter.CreateCounter<long>("fullnet.auditing.microbatch.rejected");
    private static readonly Counter<long> Flushed =
        Meter.CreateCounter<long>("fullnet.auditing.microbatch.flushed");
    private static readonly Counter<long> Failed =
        Meter.CreateCounter<long>("fullnet.auditing.microbatch.failed");
    private static readonly Counter<long> Poisoned =
        Meter.CreateCounter<long>("fullnet.auditing.microbatch.poisoned");
    private static readonly Histogram<double> WaitMs =
        Meter.CreateHistogram<double>("fullnet.auditing.microbatch.wait_ms", unit: "ms");
    private static readonly Histogram<long> BatchRows =
        Meter.CreateHistogram<long>("fullnet.auditing.microbatch.batch_rows");
    private static readonly Histogram<long> BatchBytes =
        Meter.CreateHistogram<long>("fullnet.auditing.microbatch.batch_bytes");

    public static void RecordAccepted(string kind) =>
        Try(() => Accepted.Add(1, new KeyValuePair<string, object?>("kind", kind)));

    public static void RecordRejected(string reason) =>
        Try(() => Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason)));

    public static void RecordFlushed(int rows, int bytes) =>
        Try(() =>
        {
            Flushed.Add(1);
            BatchRows.Record(rows);
            BatchBytes.Record(bytes);
        });

    public static void RecordFailed(string outcome) =>
        Try(() => Failed.Add(1, new KeyValuePair<string, object?>("outcome", outcome)));

    public static void RecordPoisoned() =>
        Try(() => Poisoned.Add(1));

    public static void RecordWait(TimeSpan wait) =>
        Try(() => WaitMs.Record(wait.TotalMilliseconds));

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // 指标旁路失败不得改变 B1 fail-open 语义。
        }
    }
}
