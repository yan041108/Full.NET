using System.Diagnostics;

namespace Full.NET.Benchmarks.Jobs;

public static class JobsBacklogIndexAbBenchmarkRunner
{
    private static readonly JobsBacklogMutationKind[] MutationKinds =
    [
        JobsBacklogMutationKind.TriggerInsert,
        JobsBacklogMutationKind.Claim,
        JobsBacklogMutationKind.TerminalSuccess,
    ];

    public static async Task RunAsync(
        JobsBacklogQueryBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Mode != JobsBacklogQueryBenchmarkMode.IndexAb)
        {
            throw new ArgumentException(
                "Jobs backlog index A/B Runner 只接受 index-ab 模式。",
                nameof(options));
        }

        var outputDirectory = Path.GetFullPath(
            options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var expectation = JobsBacklogDataset.CreateExpectation(
            options.Rows,
            options.ReferenceUtc);
        var providerResults =
            new List<JobsBacklogIndexProviderResult>();

        foreach (var providerName in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine(
                $"[{providerName}] 启动隔离容器并执行正式迁移...");
            await using var database =
                await JobsBacklogBenchmarkDatabase.StartAsync(
                    providerName,
                    cancellationToken);

            Console.WriteLine(
                $"[{providerName}] 写入 {options.Rows} 行固定分布数据...");
            var seedStopwatch = Stopwatch.StartNew();
            await database.SeedAsync(options, cancellationToken);
            seedStopwatch.Stop();

            var querySamples = CreateVariantSamples();
            var mutationSamples = CreateMutationSamples();
            var lastResults =
                new Dictionary<
                    JobsBacklogIndexVariant,
                    JobsBacklogQueryResult>();
            var planEvidence =
                new Dictionary<
                    JobsBacklogIndexVariant,
                    JobsBacklogIndexPlanEvidence>();
            var candidateIndexBuildDuration = TimeSpan.Zero;
            long candidateIndexSizeBytes = 0;
            var warmupBlocks = JobsBacklogIndexAbSampling.CreateBlocks(
                options.WarmupIterations);
            var queryBlocks = JobsBacklogIndexAbSampling.CreateBlocks(
                options.MeasurementIterations);
            var mutationBlocks =
                JobsBacklogIndexAbSampling.CreateBlocks(
                    options.MutationIterations);

            for (var blockIndex = 0;
                 blockIndex < queryBlocks.Count;
                 blockIndex++)
            {
                var queryBlock = queryBlocks[blockIndex];
                var mutationBlock = mutationBlocks[blockIndex];
                var warmupBlock = warmupBlocks[blockIndex];
                if (queryBlock.Variant != mutationBlock.Variant
                    || queryBlock.Variant != warmupBlock.Variant)
                {
                    throw new InvalidOperationException(
                        "Jobs backlog A/B 预热、查询与写路径镜像块不一致。");
                }

                var transitionDuration =
                    await database.SetIndexVariantAsync(
                        queryBlock.Variant,
                        cancellationToken);
                if (queryBlock.Variant
                    == JobsBacklogIndexVariant.Candidate)
                {
                    candidateIndexBuildDuration =
                        TimeSpan.FromTicks(Math.Max(
                            candidateIndexBuildDuration.Ticks,
                            transitionDuration.Ticks));
                    candidateIndexSizeBytes =
                        await database.GetCandidateIndexSizeBytesAsync(
                            cancellationToken);
                    if (candidateIndexSizeBytes <= 0)
                    {
                        throw new InvalidOperationException(
                            $"{providerName} 候选索引体积必须大于 0。");
                    }
                }

                Console.WriteLine(
                    $"[{providerName}] {queryBlock.Variant} "
                    + $"块 {blockIndex + 1}/4：查询 "
                    + $"{queryBlock.SampleCount}，写路径 "
                    + $"{mutationBlock.SampleCount}...");
                await WarmupAsync(
                    database,
                    warmupBlock.SampleCount,
                    options.ReferenceUtc,
                    expectation,
                    providerName,
                    cancellationToken);
                await SampleQueriesAsync(
                    database,
                    options,
                    expectation,
                    providerName,
                    queryBlock,
                    querySamples[queryBlock.Variant],
                    lastResults,
                    cancellationToken);
                await SampleMutationsAsync(
                    database,
                    options,
                    mutationBlock,
                    mutationSamples[mutationBlock.Variant],
                    cancellationToken);

                if (!planEvidence.ContainsKey(queryBlock.Variant))
                {
                    planEvidence[queryBlock.Variant] =
                        await PersistPlansAsync(
                            database,
                            outputDirectory,
                            providerName,
                            queryBlock.Variant,
                            options.ReferenceUtc,
                            cancellationToken);
                }
            }

            var baseline = CreateVariantResult(
                JobsBacklogIndexVariant.Baseline,
                options,
                lastResults,
                querySamples,
                mutationSamples,
                planEvidence);
            var candidate = CreateVariantResult(
                JobsBacklogIndexVariant.Candidate,
                options,
                lastResults,
                querySamples,
                mutationSamples,
                planEvidence);
            var assessment = JobsBacklogIndexAbAssessment.Assess(
                expectation,
                baseline,
                candidate);
            providerResults.Add(
                new JobsBacklogIndexProviderResult(
                    database.ProviderName,
                    database.ContainerImage,
                    await database.GetVersionAsync(
                        cancellationToken),
                    seedStopwatch.Elapsed,
                    candidateIndexBuildDuration,
                    candidateIndexSizeBytes,
                    expectation,
                    baseline,
                    candidate,
                    assessment));
            Console.WriteLine(
                $"[{providerName}] 迁移门禁："
                + $"{(assessment.MigrationAllowed ? "ALLOW" : "BLOCK")}");
        }

        var report = JobsBacklogIndexAbReportWriter.CreateReport(
            options,
            providerResults);
        await JobsBacklogIndexAbReportWriter.WriteAsync(
            outputDirectory,
            report,
            cancellationToken);
        Console.WriteLine($"Jobs backlog index A/B 工件：{outputDirectory}");
    }

    private static Dictionary<
        JobsBacklogIndexVariant,
        List<TimeSpan>> CreateVariantSamples() =>
        Enum.GetValues<JobsBacklogIndexVariant>()
            .ToDictionary(variant => variant, _ => new List<TimeSpan>());

    private static Dictionary<
        JobsBacklogIndexVariant,
        Dictionary<JobsBacklogMutationKind, List<TimeSpan>>>
        CreateMutationSamples() =>
        Enum.GetValues<JobsBacklogIndexVariant>()
            .ToDictionary(
                variant => variant,
                _ => MutationKinds.ToDictionary(
                    mutation => mutation,
                    _ => new List<TimeSpan>()));

    private static async Task WarmupAsync(
        JobsBacklogBenchmarkDatabase database,
        int warmupIterations,
        DateTimeOffset referenceUtc,
        JobsBacklogDatasetExpectation expectation,
        string providerName,
        CancellationToken cancellationToken)
    {
        for (var index = 0;
             index < warmupIterations;
             index++)
        {
            var result = await database.ExecuteAsync(
                referenceUtc,
                cancellationToken);
            EnsureCorrect(
                providerName,
                expectation,
                result,
                "A/B 预热");
            foreach (var mutation in MutationKinds)
            {
                await database.MeasureMutationAsync(
                    mutation,
                    referenceUtc,
                    cancellationToken);
            }
        }
    }

    private static async Task SampleQueriesAsync(
        JobsBacklogBenchmarkDatabase database,
        JobsBacklogQueryBenchmarkOptions options,
        JobsBacklogDatasetExpectation expectation,
        string providerName,
        JobsBacklogIndexSampleBlock block,
        ICollection<TimeSpan> samples,
        IDictionary<
            JobsBacklogIndexVariant,
            JobsBacklogQueryResult> lastResults,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < block.SampleCount; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await database.ExecuteAsync(
                options.ReferenceUtc,
                cancellationToken);
            stopwatch.Stop();
            EnsureCorrect(
                providerName,
                expectation,
                result,
                $"{block.Variant} 查询");
            samples.Add(stopwatch.Elapsed);
            lastResults[block.Variant] = result;
        }
    }

    private static async Task SampleMutationsAsync(
        JobsBacklogBenchmarkDatabase database,
        JobsBacklogQueryBenchmarkOptions options,
        JobsBacklogIndexSampleBlock block,
        IReadOnlyDictionary<
            JobsBacklogMutationKind,
            List<TimeSpan>> samples,
        CancellationToken cancellationToken)
    {
        foreach (var mutation in MutationKinds)
        {
            for (var index = 0; index < block.SampleCount; index++)
            {
                samples[mutation].Add(
                    await database.MeasureMutationAsync(
                        mutation,
                        options.ReferenceUtc,
                        cancellationToken));
            }
        }
    }

    private static async Task<JobsBacklogIndexPlanEvidence>
        PersistPlansAsync(
        JobsBacklogBenchmarkDatabase database,
        string outputDirectory,
        string providerName,
        JobsBacklogIndexVariant variant,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var variantName = variant.ToString().ToLowerInvariant();
        var planDirectory = Path.Combine(
            outputDirectory,
            providerName,
            variantName);
        Directory.CreateDirectory(planDirectory);
        var artifacts = await database.CapturePlansAsync(
            observedAtUtc,
            cancellationToken);
        var files = new List<string>();
        var usesCandidateIndex = false;
        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Content))
            {
                throw new InvalidOperationException(
                    $"{providerName}/{variantName}/{artifact.FileName} "
                    + "执行计划为空。");
            }

            var planPath = Path.Combine(
                planDirectory,
                artifact.FileName);
            await File.WriteAllTextAsync(
                planPath,
                artifact.Content,
                cancellationToken);
            files.Add(Path.GetRelativePath(outputDirectory, planPath));
            usesCandidateIndex |=
                JobsBacklogIndexPlanInspector.UsesCandidateIndex(
                    providerName,
                    artifact.Content);
        }

        return new JobsBacklogIndexPlanEvidence(
            files,
            usesCandidateIndex);
    }

    private static JobsBacklogIndexVariantResult CreateVariantResult(
        JobsBacklogIndexVariant variant,
        JobsBacklogQueryBenchmarkOptions options,
        IReadOnlyDictionary<
            JobsBacklogIndexVariant,
            JobsBacklogQueryResult> lastResults,
        IReadOnlyDictionary<
            JobsBacklogIndexVariant,
            List<TimeSpan>> querySamples,
        IReadOnlyDictionary<
            JobsBacklogIndexVariant,
            Dictionary<JobsBacklogMutationKind, List<TimeSpan>>>
            mutationSamples,
        IReadOnlyDictionary<
            JobsBacklogIndexVariant,
            JobsBacklogIndexPlanEvidence> planEvidence)
    {
        var queries = querySamples[variant];
        if (queries.Count != options.MeasurementIterations)
        {
            throw new InvalidOperationException(
                $"{variant} 查询样本不完整："
                + $"{queries.Count}/{options.MeasurementIterations}。");
        }

        var mutations = mutationSamples[variant];
        foreach (var (kind, samples) in mutations)
        {
            if (samples.Count != options.MutationIterations)
            {
                throw new InvalidOperationException(
                    $"{variant}/{kind} 写路径样本不完整："
                    + $"{samples.Count}/{options.MutationIterations}。");
            }
        }

        var plans = planEvidence[variant];
        return new JobsBacklogIndexVariantResult(
            variant,
            lastResults[variant],
            JobsBacklogQueryStatistics.Calculate(queries),
            queries.Select(sample => sample.TotalMilliseconds).ToArray(),
            new JobsBacklogMutationStatistics(
                JobsBacklogQueryStatistics.Calculate(
                    mutations[
                        JobsBacklogMutationKind.TriggerInsert]),
                JobsBacklogQueryStatistics.Calculate(
                    mutations[JobsBacklogMutationKind.Claim]),
                JobsBacklogQueryStatistics.Calculate(
                    mutations[
                        JobsBacklogMutationKind.TerminalSuccess])),
            plans.Files,
            plans.UsesCandidateIndex);
    }

    private static void EnsureCorrect(
        string provider,
        JobsBacklogDatasetExpectation expectation,
        JobsBacklogQueryResult result,
        string phase)
    {
        if (!result.Matches(expectation))
        {
            throw new InvalidOperationException(
                $"{provider} {phase} Jobs backlog "
                + "结果未通过正确性门禁。");
        }
    }

    private sealed record JobsBacklogIndexPlanEvidence(
        IReadOnlyList<string> Files,
        bool UsesCandidateIndex);
}
