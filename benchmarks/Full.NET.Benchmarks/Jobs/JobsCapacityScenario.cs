namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsCapacityScenario(
    int Concurrency,
    int HandlerDelayMilliseconds,
    int Replicas)
{
    public string Name =>
        $"c{Concurrency}-d{HandlerDelayMilliseconds}-r{Replicas}";
}

public static class JobsCapacityScenarioCatalog
{
    public static IReadOnlyList<JobsCapacityScenario> Build(
        JobsCapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scenarios = new HashSet<JobsCapacityScenario>();
        foreach (var concurrency in options.ConcurrencyLevels)
        {
            foreach (var delay in options.HandlerDelayMilliseconds)
            {
                scenarios.Add(new JobsCapacityScenario(
                    concurrency,
                    delay,
                    Replicas: 1));
            }
        }

        var replicaConcurrency = options.ConcurrencyLevels
            .FirstOrDefault(value => value >= 2);
        if (replicaConcurrency == 0)
        {
            replicaConcurrency = options.ConcurrencyLevels[0];
        }

        var slowestDelay = options.HandlerDelayMilliseconds.Max();
        foreach (var replicas in options.ReplicaCounts)
        {
            scenarios.Add(new JobsCapacityScenario(
                replicaConcurrency,
                slowestDelay,
                replicas));
        }

        return scenarios
            .OrderBy(scenario => scenario.Concurrency)
            .ThenBy(scenario => scenario.HandlerDelayMilliseconds)
            .ThenBy(scenario => scenario.Replicas)
            .ToArray();
    }
}
