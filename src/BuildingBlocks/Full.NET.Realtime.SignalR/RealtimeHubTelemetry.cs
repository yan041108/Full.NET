using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Realtime.SignalR.Health;

namespace Full.NET.Realtime.SignalR;

/// <summary>记录 Hub 分组授权与执行结果的低基数指标，不包含用户、租户或连接标识。</summary>
internal static class RealtimeHubTelemetry
{
    private static readonly Meter Meter =
        new(RealtimeBackplaneTelemetry.MeterName);
    private static readonly Counter<long> AuthorizationDecisions =
        Meter.CreateCounter<long>(
            "fullnet.realtime.hub.authorization.decisions",
            unit: "{decision}");
    private static readonly Counter<long> GroupAssignments =
        Meter.CreateCounter<long>(
            "fullnet.realtime.hub.group.assignments",
            unit: "{assignment}");
    private static readonly UpDownCounter<long> ActiveConnections =
        Meter.CreateUpDownCounter<long>(
            "fullnet.realtime.hub.connections.active",
            unit: "{connection}");
    private static readonly Histogram<double> ConnectionDuration =
        Meter.CreateHistogram<double>(
            "fullnet.realtime.hub.connection.duration",
            unit: "ms");
    private static readonly Histogram<double> GroupAssignmentDuration =
        Meter.CreateHistogram<double>(
            "fullnet.realtime.hub.group.assignment.duration",
            unit: "ms");

    public static void RecordAuthorizationDecision(string outcome)
    {
        try
        {
            AuthorizationDecisions.Add(
                1,
                new KeyValuePair<string, object?>(
                    "outcome",
                    outcome));
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得扩大或阻断已经作出的分组授权决策。
        }
    }

    public static void RecordGroupAssignment(
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
            GroupAssignments.Add(1, tags);
            GroupAssignmentDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds,
                tags);
        }
        catch (Exception)
        {
            // 指标消费失败属于旁路故障，不得覆盖分组结果或改变连接生命周期。
        }
    }

    public static void RecordActiveConnection(long delta)
    {
        try
        {
            ActiveConnections.Add(delta);
        }
        catch (Exception)
        {
            // 活跃连接指标属于旁路，导出失败不得改变连接建立或断开语义。
        }
    }

    public static void RecordConnectionDuration(
        long startedTimestamp,
        string outcome)
    {
        try
        {
            ConnectionDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds,
                new KeyValuePair<string, object?>(
                    "outcome",
                    outcome));
        }
        catch (Exception)
        {
            // 连接时长指标属于旁路，不得让导出失败覆盖断开回调或其原始异常。
        }
    }
}
