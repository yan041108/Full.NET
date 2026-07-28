namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingQueryStatistics(
    int SampleCount,
    double MinimumMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds)
{
    public static AuditingQueryStatistics Calculate(IReadOnlyCollection<TimeSpan> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("至少需要一个耗时样本。", nameof(samples));
        }

        var ordered = samples
            .Select(sample => sample.TotalMilliseconds)
            .Order()
            .ToArray();

        return new AuditingQueryStatistics(
            ordered.Length,
            ordered[0],
            NearestRank(ordered, 0.50),
            NearestRank(ordered, 0.95),
            NearestRank(ordered, 0.99),
            ordered[^1]);
    }

    private static double NearestRank(IReadOnlyList<double> ordered, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * ordered.Count);
        return ordered[Math.Max(0, rank - 1)];
    }
}
