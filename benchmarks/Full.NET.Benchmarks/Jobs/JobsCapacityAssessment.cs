namespace Full.NET.Benchmarks.Jobs;

public enum JobsCapacityRecommendation
{
    KeepConcurrencyOne = 0,
    EligibleForCanaryAtTwo = 1,
}

public sealed record JobsCapacityAssessmentResult(
    JobsCapacityRecommendation Recommendation,
    IReadOnlyList<string> Reasons);

public static class JobsCapacityAssessment
{
    private static readonly JobsCapacityOptions DecisionMatrixBaseline =
        JobsCapacityOptions.Parse([]);

    private static readonly string[] RequiredProviders =
    [
        "sqlserver",
        "mysql",
    ];

    public static JobsCapacityAssessmentResult Assess(
        JobsCapacityOptions options,
        IReadOnlyList<JobsCapacityScenario> scenarios,
        IReadOnlyCollection<JobsCapacityRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(runs);
        var reasons = new List<string>();
        if (!HasCanaryDecisionMatrix(options, scenarios))
        {
            reasons.Add("容量证据未使用完整手工决策矩阵。");
        }

        if (!HasCompleteMatrix(options, scenarios, runs))
        {
            reasons.Add("容量矩阵证据不完整或包含重复、越界样本键。");
        }

        foreach (var requiredProvider in RequiredProviders)
        {
            var providerRuns = runs
                .Where(run => string.Equals(
                    run.Provider,
                    requiredProvider,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (providerRuns.Length == 0)
            {
                reasons.Add($"缺少 {requiredProvider} 容量证据。");
                continue;
            }

            AssessProvider(requiredProvider, providerRuns, reasons);
        }

        if (runs.Any(run => !RequiredProviders.Contains(
                run.Provider,
                StringComparer.OrdinalIgnoreCase)))
        {
            reasons.Add("容量证据包含未支持的 Provider。");
        }

        return new JobsCapacityAssessmentResult(
            reasons.Count == 0
                ? JobsCapacityRecommendation.EligibleForCanaryAtTwo
                : JobsCapacityRecommendation.KeepConcurrencyOne,
            reasons);
    }

    private static bool HasCanaryDecisionMatrix(
        JobsCapacityOptions options,
        IReadOnlyList<JobsCapacityScenario> scenarios)
    {
        var baseline = DecisionMatrixBaseline;
        return options.Providers.Count == baseline.Providers.Count
            && baseline.Providers.All(provider =>
                options.Providers.Contains(
                    provider,
                    StringComparer.OrdinalIgnoreCase))
            && baseline.ConcurrencyLevels.All(
                options.ConcurrencyLevels.Contains)
            && baseline.HandlerDelayMilliseconds.All(
                options.HandlerDelayMilliseconds.Contains)
            && baseline.ReplicaCounts.All(options.ReplicaCounts.Contains)
            && options.Repetitions >= baseline.Repetitions
            && options.Warmup >= baseline.Warmup
            && options.Duration >= baseline.Duration
            && options.SeedJobs >= baseline.SeedJobs
            && options.BatchSize == baseline.BatchSize
            && options.HandlerKeyCount == baseline.HandlerKeyCount
            && options.FailingHandlerKeyCount
                == baseline.FailingHandlerKeyCount
            && options.Lease == baseline.Lease
            && options.LeaseRenewal == baseline.LeaseRenewal
            && scenarios.SequenceEqual(
                JobsCapacityScenarioCatalog.Build(options));
    }

    internal static bool HasCompleteMatrix(
        JobsCapacityOptions options,
        IReadOnlyList<JobsCapacityScenario> scenarios,
        IReadOnlyCollection<JobsCapacityRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(runs);
        var expectedCount = checked(
            options.Providers.Count
            * scenarios.Count
            * options.Repetitions);
        var expectedKeys = options.Providers
            .SelectMany(provider => scenarios.SelectMany(scenario =>
                Enumerable.Range(1, options.Repetitions).Select(repetition =>
                    new JobsCapacityRunKey(
                        provider.ToLowerInvariant(),
                        scenario,
                        repetition))))
            .ToHashSet();
        var actualKeys = runs
            .Select(run => new JobsCapacityRunKey(
                run.Provider.ToLowerInvariant(),
                run.Scenario,
                run.Repetition))
            .ToHashSet();
        return expectedKeys.Count == expectedCount
            && actualKeys.Count == runs.Count
            && actualKeys.SetEquals(expectedKeys);
    }

    private static void AssessProvider(
        string provider,
        IReadOnlyList<JobsCapacityRunResult> runs,
        ICollection<string> reasons)
    {
        var c2Runs = runs
            .Where(run => run.Scenario.Concurrency == 2)
            .ToArray();
        if (c2Runs.Length == 0
            || c2Runs.Any(run => !run.CorrectnessGatePassed))
        {
            reasons.Add($"{provider} 的 c2 正确性门禁未全部通过。");
        }

        var singleReplica = runs
            .Where(run => run.Scenario.Replicas == 1)
            .ToArray();
        foreach (var delay in singleReplica
                     .Select(run =>
                         run.Scenario.HandlerDelayMilliseconds)
                     .Distinct()
                     .Order())
        {
            var c1 = singleReplica
                .Where(run =>
                    run.Scenario.Concurrency == 1
                    && run.Scenario.HandlerDelayMilliseconds == delay)
                .ToArray();
            var c2 = singleReplica
                .Where(run =>
                    run.Scenario.Concurrency == 2
                    && run.Scenario.HandlerDelayMilliseconds == delay)
                .ToArray();
            if (c1.Length == 0
                || c2.Length == 0
                || c1.Any(run => !run.CorrectnessGatePassed)
                || c2.Any(run => !run.CorrectnessGatePassed))
            {
                reasons.Add(
                    $"{provider} 缺少 delay={delay} 的可比 c1/c2 正确样本。");
                continue;
            }

            var c1Throughput = Median(
                c1.Select(run => run.TerminalsPerSecond));
            var c2Throughput = Median(
                c2.Select(run => run.TerminalsPerSecond));
            if (c2Throughput < c1Throughput * 1.20d)
            {
                reasons.Add(
                    $"{provider} delay={delay} 的 c2 吞吐提升不足 20%。");
            }

            if (c1.Any(run => run.QueueLatency is null)
                || c2.Any(run => run.QueueLatency is null)
                || Median(c2.Select(run =>
                        run.QueueLatency!.P95Milliseconds))
                    > Median(c1.Select(run =>
                        run.QueueLatency!.P95Milliseconds)))
            {
                reasons.Add(
                    $"{provider} delay={delay} 的 c2 队列 P95 缺失或回退。");
            }
        }

        if (runs.Count == 0)
        {
            return;
        }

        var slowestDelay = runs.Max(run =>
            run.Scenario.HandlerDelayMilliseconds);
        foreach (var concurrency in new[] { 1, 2 })
        {
            var slowRuns = singleReplica
                .Where(run =>
                    run.Scenario.Concurrency == concurrency
                    && run.Scenario.HandlerDelayMilliseconds
                        == slowestDelay)
                .ToArray();
            if (slowRuns.Length == 0
                || slowRuns.Any(run =>
                    run.LeaseRenewalExecutions <= 0))
            {
                reasons.Add(
                    $"{provider} 慢 Handler c{concurrency} 缺少续租证据。");
            }
        }

        var multiReplicaC2 = runs
            .Where(run =>
                run.Scenario.Concurrency == 2
                && run.Scenario.HandlerDelayMilliseconds
                    == slowestDelay
                && run.Scenario.Replicas >= 2)
            .ToArray();
        if (multiReplicaC2.Length == 0
            || multiReplicaC2.Any(run =>
                !run.CorrectnessGatePassed))
        {
            reasons.Add(
                $"{provider} 缺少慢 Handler 多副本 c2 正确性证据。");
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("中位数至少需要一个样本。");
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private sealed record JobsCapacityRunKey(
        string Provider,
        JobsCapacityScenario Scenario,
        int Repetition);
}
