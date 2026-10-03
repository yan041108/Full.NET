using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Full.NET.LogConsumer;

/// <summary>独立消费者的固定运维状态；未知统计失败关闭，指标不携带原始来源或事件。</summary>
internal sealed class ConsumerRuntimeState
{
    private sealed record Assignment(int Count, HashSet<(string Topic, int Partition)> Keys);
    private sealed record LagSample(Assignment Assignment, long Records, long UnixSeconds);
    private Assignment _assignment = new(0, []);
    private LagSample? _sample;
    private int _stopping;
    private long _committed;
    private long _deferred;
    private readonly long[] _results = new long[9];
    private static readonly (string Stage, string Outcome)[] Series =
    [
        ("es", "confirmed"), ("es", "retry"), ("es", "isolated"), ("es", "error"),
        ("dlq", "confirmed"), ("dlq", "retry"), ("dlq", "error"),
        ("offset", "confirmed"), ("offset", "error"),
    ];

    public bool IsStopping => Volatile.Read(ref _stopping) != 0;
    public bool IsReady => !IsStopping && Volatile.Read(ref _assignment).Count > 0;

    public void Assign(IReadOnlyCollection<(string Topic, int Partition)> partitions)
    {
        // 限制本指标持有的身份集合；超出上限仍可消费，但不能把不完整聚合当成可信积压。
        Volatile.Write(ref _assignment, new Assignment(partitions.Count,
            partitions.Count <= 128 ? partitions.ToHashSet() : []));
        Volatile.Write(ref _sample, null);
    }

    public void Stop() => Interlocked.Exchange(ref _stopping, 1);
    public void Committed() => Interlocked.Increment(ref _committed);
    public void Deferred() => Interlocked.Increment(ref _deferred);
    public void Record(ConsumerDeliveryMetric metric) => Interlocked.Increment(ref _results[(int)metric]);

    /// <summary>读取 SDK 已生成的只读统计，不执行网络查询；格式或预算异常不得影响消费。</summary>
    public void Statistics(string json)
    {
        Volatile.Write(ref _sample, null);
        var assignment = Volatile.Read(ref _assignment);
        if (assignment.Count is < 1 or > 128 || json.Length > 262144) return;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.GetProperty("cgrp").GetProperty("assignment_size").GetInt32() != assignment.Count) return;
            var generatedAt = root.GetProperty("time").GetInt64();
            var lag = 0L;
            var seen = new HashSet<(string Topic, int Partition)>();
            var availableBrokers = new HashSet<int>();
            foreach (var broker in root.GetProperty("brokers").EnumerateObject())
            {
                var value = broker.Value;
                if (value.GetProperty("state").GetString() == "UP")
                    availableBrokers.Add(value.GetProperty("nodeid").GetInt32());
            }
            var topics = root.GetProperty("topics");
            foreach (var topic in topics.EnumerateObject())
            foreach (var partition in topic.Value.GetProperty("partitions").EnumerateObject())
            {
                var value = partition.Value;
                var key = (topic.Name, value.GetProperty("partition").GetInt32());
                if (!assignment.Keys.Contains(key)) continue;
                // SDK 的统计时间仍可前进而 Broker 已断开，不能把缓存的零积压刷新为可信值。
                if (!availableBrokers.Contains(value.GetProperty("broker").GetInt32())) return;
                // consumer_lag 基于已提交 Offset；stored/app Offset 会误把未确认事件当作消费完成。
                var records = value.GetProperty("consumer_lag").GetInt64();
                if (records < 0 || !seen.Add(key)) return;
                lag = checked(lag + records);
            }
            if (seen.Count != assignment.Count) return;
            Volatile.Write(ref _sample, new LagSample(assignment, lag, generatedAt));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException)
        {
            // 不输出统计正文，避免 Broker、Topic 或认证信息泄漏到递归日志。
        }
    }

    public string Metrics()
    {
        var assignment = Volatile.Read(ref _assignment);
        var sample = Volatile.Read(ref _sample);
        var age = sample is null ? -1 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() - sample.UnixSeconds;
        var known = IsReady && sample is not null && ReferenceEquals(sample.Assignment, assignment) && age is >= 0 and <= 30;
        var result = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"""
            # TYPE fullnet_log_consumer_assigned_partitions gauge
            fullnet_log_consumer_assigned_partitions {assignment.Count}
            # TYPE fullnet_log_consumer_ready gauge
            fullnet_log_consumer_ready {(IsReady ? 1 : 0)}
            # TYPE fullnet_log_consumer_committed_records_total counter
            fullnet_log_consumer_committed_records_total {Interlocked.Read(ref _committed)}
            # TYPE fullnet_log_consumer_deferred_records_total counter
            fullnet_log_consumer_deferred_records_total {Interlocked.Read(ref _deferred)}
            # TYPE fullnet_log_consumer_lag_records gauge
            fullnet_log_consumer_lag_records {(known ? sample!.Records : -1)}
            # TYPE fullnet_log_consumer_lag_known gauge
            fullnet_log_consumer_lag_known {(known ? 1 : 0)}
            # TYPE fullnet_log_consumer_lag_sample_age_seconds gauge
            fullnet_log_consumer_lag_sample_age_seconds {age}
            # TYPE fullnet_log_consumer_delivery_results_total counter

            """));
        for (var index = 0; index < Series.Length; index++)
        {
            var series = Series[index];
            result.Append(CultureInfo.InvariantCulture,
                $"fullnet_log_consumer_delivery_results_total{{stage=\"{series.Stage}\",outcome=\"{series.Outcome}\"}} {Interlocked.Read(ref _results[index])}\n");
        }
        return result.ToString();
    }
}

/// <summary>固定九种投递结果；不得将动态错误、身份或事件内容转换成标签。</summary>
internal enum ConsumerDeliveryMetric
{
    EsConfirmed, EsRetry, EsIsolated, EsError,
    DlqConfirmed, DlqRetry, DlqError,
    OffsetConfirmed, OffsetError,
}
