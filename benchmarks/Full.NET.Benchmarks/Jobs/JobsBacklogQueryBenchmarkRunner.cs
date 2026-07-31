using System.Diagnostics;

namespace Full.NET.Benchmarks.Jobs;

public static class JobsBacklogQueryBenchmarkRunner
{
    public static async Task RunAsync(
        JobsBacklogQueryBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var outputDirectory = Path.GetFullPath(
            options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var expectation = JobsBacklogDataset.CreateExpectation(
            options.Rows,
            options.ReferenceUtc);
        var providerResults =
            new List<JobsBacklogQueryProviderResult>();

        foreach (var providerName in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine(
                $"[{providerName}] 启动容器并执行正式迁移...");
            await using var database =
                await JobsBacklogBenchmarkDatabase.StartAsync(
                    providerName,
                    cancellationToken);

            Console.WriteLine(
                $"[{providerName}] 写入 {options.Rows} 行固定分布数据...");
            var seedStopwatch = Stopwatch.StartNew();
            await database.SeedAsync(options, cancellationToken);
            seedStopwatch.Stop();

            Console.WriteLine(
                $"[{providerName}] 预热 {options.WarmupIterations}，"
                + $"采样 {options.MeasurementIterations}...");
            for (var index = 0;
                 index < options.WarmupIterations;
                 index++)
            {
                var warmupResult = await database.ExecuteAsync(
                    options.ReferenceUtc,
                    cancellationToken);
                EnsureCorrect(
                    providerName,
                    expectation,
                    warmupResult,
                    "预热");
            }

            var samples =
                new List<TimeSpan>(options.MeasurementIterations);
            JobsBacklogQueryResult? lastResult = null;
            for (var index = 0;
                 index < options.MeasurementIterations;
                 index++)
            {
                var stopwatch = Stopwatch.StartNew();
                lastResult = await database.ExecuteAsync(
                    options.ReferenceUtc,
                    cancellationToken);
                stopwatch.Stop();
                EnsureCorrect(
                    providerName,
                    expectation,
                    lastResult,
                    $"采样 {index + 1}");
                samples.Add(stopwatch.Elapsed);
            }

            var planDirectory = Path.Combine(
                outputDirectory,
                providerName);
            Directory.CreateDirectory(planDirectory);
            var planArtifacts = await database.CapturePlansAsync(
                options.ReferenceUtc,
                cancellationToken);
            var planFiles = new List<string>();
            foreach (var artifact in planArtifacts)
            {
                if (string.IsNullOrWhiteSpace(artifact.Content))
                {
                    throw new InvalidOperationException(
                        $"{providerName}/{artifact.FileName} 执行计划为空。");
                }

                var planPath = Path.Combine(
                    planDirectory,
                    artifact.FileName);
                await File.WriteAllTextAsync(
                    planPath,
                    artifact.Content,
                    cancellationToken);
                planFiles.Add(
                    Path.GetRelativePath(
                        outputDirectory,
                        planPath));
            }

            var statistics =
                JobsBacklogQueryStatistics.Calculate(samples);
            Console.WriteLine(
                $"[{providerName}] P50="
                + $"{statistics.P50Milliseconds:0.###} ms，"
                + $"P95={statistics.P95Milliseconds:0.###} ms，"
                + $"P99={statistics.P99Milliseconds:0.###} ms");
            providerResults.Add(
                new JobsBacklogQueryProviderResult(
                    database.ProviderName,
                    database.ContainerImage,
                    await database.GetVersionAsync(
                        cancellationToken),
                    seedStopwatch.Elapsed,
                    expectation,
                    lastResult
                        ?? throw new InvalidOperationException(
                            "采样未产生查询结果。"),
                    statistics,
                    samples
                        .Select(sample => sample.TotalMilliseconds)
                        .ToArray(),
                    planFiles));
        }

        var report = JobsBacklogQueryReportWriter.CreateReport(
            options,
            providerResults);
        await JobsBacklogQueryReportWriter.WriteAsync(
            outputDirectory,
            report,
            cancellationToken);
        Console.WriteLine($"基准工件：{outputDirectory}");
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
                $"{provider} {phase} Jobs backlog 结果未通过正确性门禁："
                + $"pending={result.PendingCount}/"
                + $"{expectation.PendingCount}，due="
                + $"{result.DueRetryCount}/"
                + $"{expectation.DueRetryCount}。");
        }
    }
}
