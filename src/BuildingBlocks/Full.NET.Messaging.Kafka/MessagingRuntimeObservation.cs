using System.Diagnostics.Metrics;

namespace Full.NET.Messaging.Kafka;

/// <summary>每个采集器独占样本状态；单调时间控制新鲜度，避免失败和系统时钟回拨保留旧健康值。</summary>
internal sealed class MessagingRuntimeObservation(TimeProvider time, TimeSpan maximumAge)
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Component, string Code), Sample> _samples = [];

    internal void Set(string component, string code, string result, double? value = null)
    {
        lock (_gate) _samples[(component, code)] = new(result, value, time.GetTimestamp(), time.GetUtcNow().ToUnixTimeSeconds());
    }

    internal IEnumerable<Measurement<long>> ObserveStatus()
    {
        lock (_gate)
        {
            return _samples.Select(item => new Measurement<long>(1,
                new KeyValuePair<string, object?>("component", item.Key.Component),
                new("resource_code", item.Key.Code), new("result", State(item.Value)))).ToArray();
        }
    }

    internal IEnumerable<Measurement<double>> ObserveValue(string component)
    {
        lock (_gate)
        {
            return _samples.Where(item => item.Key.Component == component && item.Value.Value is not null
                    && State(item.Value) is "healthy" or "unhealthy")
                .Select(item => new Measurement<double>(item.Value.Value!.Value,
                    new KeyValuePair<string, object?>("resource_code", item.Key.Code))).ToArray();
        }
    }

    // Collector 缓存旧 OTLP 样本时应用回调不会再运行；暴露原始观测时间和预算，使 Prometheus 自己判定陈旧。
    internal IEnumerable<Measurement<double>> ObserveTimestamps(bool ageLimit = false)
    {
        lock (_gate)
        {
            return _samples.Where(item => item.Value.Result != "disabled").Select(item => new Measurement<double>(
                ageLimit ? maximumAge.TotalSeconds : item.Value.UnixTimeSeconds,
                new KeyValuePair<string, object?>("component", item.Key.Component), new("resource_code", item.Key.Code))).ToArray();
        }
    }

    private string State(Sample sample) => sample.Result is "disabled" ? "disabled"
        : time.GetElapsedTime(sample.Timestamp) > maximumAge ? "stale" : sample.Result;

    private sealed record Sample(string Result, double? Value, long Timestamp, long UnixTimeSeconds);
}
