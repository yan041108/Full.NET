namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsCapacityStatistics(
    int SampleCount,
    double MinimumMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds)
{
    public static JobsCapacityStatistics Calculate(
        IReadOnlyCollection<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException(
                "至少需要一个延迟样本。",
                nameof(samples));
        }

        var ordered = samples.Order().ToArray();
        if (ordered.Any(value =>
                !double.IsFinite(value) || value < 0d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                "延迟样本必须是非负有限值。");
        }

        return new JobsCapacityStatistics(
            ordered.Length,
            ordered[0],
            NearestRank(ordered, 0.50d),
            NearestRank(ordered, 0.95d),
            NearestRank(ordered, 0.99d),
            ordered[^1]);
    }

    private static double NearestRank(
        IReadOnlyList<double> ordered,
        double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * ordered.Count);
        return ordered[Math.Max(0, rank - 1)];
    }
}
