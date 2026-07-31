namespace Full.NET.Benchmarks.Jobs;

public static class JobsCapacityBacklogPlanner
{
    private const double SafetyFactor = 1.5d;
    private const int MaximumJobs = 1_000_000;

    public static int CalculateRequiredJobs(
        int configuredMinimum,
        long completedDuringWarmup,
        TimeSpan warmup,
        TimeSpan duration,
        int batchSize,
        int replicas)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            configuredMinimum);
        ArgumentOutOfRangeException.ThrowIfNegative(completedDuringWarmup);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            warmup,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            duration,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(replicas);

        var measuredRate =
            completedDuringWarmup / warmup.TotalSeconds;
        var measuredNeed = checked((long)Math.Ceiling(
            measuredRate
            * duration.TotalSeconds
            * SafetyFactor));
        var drainReserve = checked((long)batchSize * replicas * 2);
        var required = Math.Max(
            configuredMinimum,
            checked(measuredNeed + drainReserve));
        if (required > MaximumJobs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedDuringWarmup),
                required,
                $"规划任务数不得超过 {MaximumJobs}。");
        }

        return checked((int)required);
    }
}
