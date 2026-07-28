namespace Full.NET.Benchmarks.Outbox;

public sealed record OutboxCapacityScenario(
    int Concurrency,
    int HandlerDelayMilliseconds,
    int Replicas,
    int BatchSize,
    int PayloadSize)
{
    public string Name =>
        $"c{Concurrency}-d{HandlerDelayMilliseconds}-r{Replicas}"
        + $"-b{BatchSize}-p{PayloadSize}";
}

public static class OutboxCapacityScenarioCatalog
{
    public static IReadOnlyList<OutboxCapacityScenario> Build(
        OutboxCapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var referenceBatchSize = options.BatchSizes[0];
        var referencePayloadSize = options.PayloadSizes[0];
        var scenarios = new HashSet<OutboxCapacityScenario>();
        foreach (var concurrency in options.ConcurrencyLevels)
        {
            foreach (var handlerDelay in options.HandlerDelayMilliseconds)
            {
                foreach (var replicas in options.ReplicaCounts)
                {
                    scenarios.Add(new OutboxCapacityScenario(
                        concurrency,
                        handlerDelay,
                        replicas,
                        referenceBatchSize,
                        referencePayloadSize));
                }
            }
        }

        var shapeConcurrency = options.ConcurrencyLevels[^1];
        var shapeDelay = options.HandlerDelayMilliseconds
            .FirstOrDefault(delay => delay > 0);
        var shapeReplicas = options.ReplicaCounts[^1];
        foreach (var batchSize in options.BatchSizes)
        {
            foreach (var payloadSize in options.PayloadSizes)
            {
                scenarios.Add(new OutboxCapacityScenario(
                    shapeConcurrency,
                    shapeDelay,
                    shapeReplicas,
                    batchSize,
                    payloadSize));
            }
        }

        return scenarios
            .OrderBy(scenario => scenario.Concurrency)
            .ThenBy(scenario => scenario.HandlerDelayMilliseconds)
            .ThenBy(scenario => scenario.Replicas)
            .ThenBy(scenario => scenario.BatchSize)
            .ThenBy(scenario => scenario.PayloadSize)
            .ToArray();
    }
}
