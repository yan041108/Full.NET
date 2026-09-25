using System.Diagnostics.Metrics;

namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>B2 访问日志队列的低基数计数器；丢弃和数据库故障不得影响请求。</summary>
internal static class AccessLogTelemetry
{
    public const string MeterName = "Full.NET.Auditing.AccessLog";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Accepted =
        Meter.CreateCounter<long>("fullnet.auditing.access_log.accepted");
    private static readonly Counter<long> Dropped =
        Meter.CreateCounter<long>("fullnet.auditing.access_log.dropped");
    private static readonly Counter<long> Written =
        Meter.CreateCounter<long>("fullnet.auditing.access_log.written");

    public static void RecordAccepted() => Try(() => Accepted.Add(1));

    public static void RecordDropped(string reason, int count = 1) =>
        Try(() => Dropped.Add(count, new KeyValuePair<string, object?>("reason", reason)));

    public static void RecordWritten(int rows) => Try(() => Written.Add(rows));

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // 指标故障不能改变 B2 访问日志的尽力写入语义。
        }
    }
}
