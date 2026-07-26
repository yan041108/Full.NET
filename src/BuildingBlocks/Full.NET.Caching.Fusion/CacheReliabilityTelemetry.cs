using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.Caching.Fusion;

/// <summary>记录缓存失效与跨节点恢复的低基数可靠性指标。</summary>
public static class CacheReliabilityTelemetry
{
    public const string MeterName = "Full.NET.Caching.Reliability";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> InvalidationDuration =
        Meter.CreateHistogram<double>(
            "fullnet.cache.invalidation.duration",
            unit: "ms");
    private static readonly Counter<long> InvalidationFailures =
        Meter.CreateCounter<long>("fullnet.cache.invalidation.failures");
    private static readonly Counter<long> StaleHits =
        Meter.CreateCounter<long>("fullnet.cache.hits.stale");
    private static readonly Counter<long> BackplaneCircuitTransitions =
        Meter.CreateCounter<long>(
            "fullnet.cache.backplane.circuit.transitions");
    private static readonly Counter<long> BackplaneRecoveries =
        Meter.CreateCounter<long>("fullnet.cache.backplane.recoveries");

    /// <summary>记录仅修复当前节点的缓存失效结果。</summary>
    public static void RecordLocalInvalidation(
        TimeSpan duration,
        bool succeeded) =>
        RecordInvalidation("local", duration, succeeded);

    /// <summary>记录必须等待 L2 与 Backplane 完成的缓存失效结果。</summary>
    public static void RecordDistributedInvalidation(
        TimeSpan duration,
        bool succeeded) =>
        RecordInvalidation("distributed", duration, succeeded);

    internal static void RecordStaleHit()
    {
        try
        {
            StaleHits.Add(1);
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得中断缓存命中或业务请求。
        }
    }

    internal static void RecordBackplaneCircuitTransition(bool isClosed)
    {
        try
        {
            BackplaneCircuitTransitions.Add(
                1,
                new KeyValuePair<string, object?>(
                    "state",
                    isClosed ? "closed" : "open"));
            if (isClosed)
            {
                BackplaneRecoveries.Add(1);
            }
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得干扰 Backplane 熔断器状态切换。
        }
    }

    private static void RecordInvalidation(
        string scope,
        TimeSpan duration,
        bool succeeded)
    {
        try
        {
            var outcome = succeeded ? "success" : "failure";
            var tags = new TagList
            {
                { "scope", scope },
                { "outcome", outcome },
            };
            InvalidationDuration.Record(duration.TotalMilliseconds, tags);
            if (!succeeded)
            {
                InvalidationFailures.Add(1, tags);
            }
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得覆盖缓存失效结果或原始异常。
        }
    }
}
