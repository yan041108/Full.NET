namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 使用约百分之一相对精度固定桶记录容量测试延迟。
/// </summary>
public sealed class KafkaCapacityLatencyHistogram
{
    public const long MinimumMicroseconds = 1;
    public const long MaximumMicroseconds = 3_600_000_000;

    private const double BucketRatio = 1.01d;
    private static readonly double LogBucketRatio = Math.Log(BucketRatio);
    private static readonly int BucketCount =
        BucketIndex(MaximumMicroseconds) + 1;

    private readonly ThreadLocal<LocalBuckets> localBuckets =
        new(static () => new LocalBuckets(BucketCount), trackAllValues: true);
    private readonly object mergeLock = new();
    private readonly long[] mergedBuckets = new long[BucketCount];
    private long mergedCount;
    private long mergedMinimum = long.MaxValue;
    private long mergedMaximum;
    private long overflowCount;

    /// <summary>
    /// 记录微秒延迟；范围外值计入 Overflow 并使证据无效。
    /// </summary>
    public bool RecordMicroseconds(long value)
    {
        if (value is < MinimumMicroseconds or > MaximumMicroseconds)
        {
            Interlocked.Increment(ref overflowCount);
            return false;
        }

        var local = localBuckets.Value!;
        local.Buckets[BucketIndex(value)]++;
        local.Count++;
        local.Minimum = Math.Min(local.Minimum, value);
        local.Maximum = Math.Max(local.Maximum, value);
        return true;
    }

    /// <summary>
    /// 将另一实例的当前快照合并到本实例的稳定聚合区。
    /// </summary>
    public void Merge(KafkaCapacityLatencyHistogram other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var aggregation = other.Capture();
        lock (mergeLock)
        {
            for (var index = 0; index < mergedBuckets.Length; index++)
            {
                mergedBuckets[index] += aggregation.Buckets[index];
            }

            mergedCount += aggregation.Count;
            mergedMinimum = Math.Min(mergedMinimum, aggregation.Minimum);
            mergedMaximum = Math.Max(mergedMaximum, aggregation.Maximum);
            Interlocked.Add(ref overflowCount, aggregation.OverflowCount);
        }
    }

    /// <summary>
    /// 合并线程局部桶并生成 P50、P95 和 P99 快照。
    /// </summary>
    public KafkaCapacityLatencySnapshot Snapshot()
    {
        var aggregation = Capture();
        if (aggregation.Count == 0)
        {
            return new KafkaCapacityLatencySnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                aggregation.OverflowCount);
        }

        return new KafkaCapacityLatencySnapshot(
            aggregation.Count,
            aggregation.Minimum,
            aggregation.Maximum,
            Quantile(aggregation.Buckets, aggregation.Count, 0.50d),
            Quantile(aggregation.Buckets, aggregation.Count, 0.95d),
            Quantile(aggregation.Buckets, aggregation.Count, 0.99d),
            aggregation.OverflowCount);
    }

    private Aggregation Capture()
    {
        var buckets = new long[BucketCount];
        long count;
        long minimum;
        long maximum;
        long overflow;
        lock (mergeLock)
        {
            Array.Copy(mergedBuckets, buckets, BucketCount);
            count = mergedCount;
            minimum = mergedMinimum;
            maximum = mergedMaximum;
            overflow = Volatile.Read(ref overflowCount);
        }

        foreach (var local in localBuckets.Values)
        {
            for (var index = 0; index < buckets.Length; index++)
            {
                buckets[index] += Volatile.Read(ref local.Buckets[index]);
            }

            count += Volatile.Read(ref local.Count);
            minimum = Math.Min(minimum, Volatile.Read(ref local.Minimum));
            maximum = Math.Max(maximum, Volatile.Read(ref local.Maximum));
        }

        return new Aggregation(buckets, count, minimum, maximum, overflow);
    }

    private static int BucketIndex(long value) =>
        (int)Math.Floor(Math.Log(value) / LogBucketRatio);

    private static long Quantile(long[] buckets, long count, double quantile)
    {
        var rank = (long)Math.Ceiling(count * quantile);
        long cumulative = 0;
        for (var index = 0; index < buckets.Length; index++)
        {
            cumulative += buckets[index];
            if (cumulative >= rank)
            {
                return Math.Min(
                    MaximumMicroseconds,
                    (long)Math.Ceiling(Math.Pow(BucketRatio, index + 1)));
            }
        }

        return MaximumMicroseconds;
    }

    private sealed class LocalBuckets(int bucketCount)
    {
        public long[] Buckets { get; } = new long[bucketCount];

        public long Count;

        public long Minimum = long.MaxValue;

        public long Maximum;
    }

    private sealed record Aggregation(
        long[] Buckets,
        long Count,
        long Minimum,
        long Maximum,
        long OverflowCount);
}
