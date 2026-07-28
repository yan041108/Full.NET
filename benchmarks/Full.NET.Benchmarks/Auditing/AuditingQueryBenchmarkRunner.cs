using System.Diagnostics;

namespace Full.NET.Benchmarks.Auditing;

public static class AuditingQueryBenchmarkRunner
{
    public static async Task RunAsync(
        AuditingQueryBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var scenarios = AuditingQueryScenarios.Create(options, options.ReferenceUtc);
        var providerResults = new List<AuditingQueryProviderResult>();

        foreach (var providerName in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"[{providerName}] 启动容器并执行正式迁移...");
            await using var database = await AuditingBenchmarkDatabase.StartAsync(
                providerName,
                cancellationToken);

            Console.WriteLine($"[{providerName}] 写入 {options.Rows} 行确定性分布数据...");
            var seedStopwatch = Stopwatch.StartNew();
            await database.SeedAsync(options.Rows, options.ReferenceUtc, cancellationToken);
            seedStopwatch.Stop();

            var scenarioResults = new List<AuditingQueryScenarioResult>();
            foreach (var scenario in scenarios)
            {
                Console.WriteLine(
                    $"[{providerName}] {scenario.Name}: "
                    + $"预热 {options.WarmupIterations}，采样 {options.MeasurementIterations}...");
                for (var index = 0; index < options.WarmupIterations; index++)
                {
                    await database.ExecutePageAsync(scenario, cancellationToken);
                }

                var samples = new List<TimeSpan>(options.MeasurementIterations);
                AuditingQueryPageResult? lastResult = null;
                for (var index = 0; index < options.MeasurementIterations; index++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    lastResult = await database.ExecutePageAsync(scenario, cancellationToken);
                    stopwatch.Stop();
                    samples.Add(stopwatch.Elapsed);
                }

                var planDirectory = Path.Combine(
                    outputDirectory,
                    providerName,
                    scenario.Name);
                Directory.CreateDirectory(planDirectory);
                var plans = await database.CapturePlansAsync(scenario, cancellationToken);
                var planFiles = new List<string>();
                foreach (var (statement, content) in plans)
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException(
                            $"{providerName}/{scenario.Name}/{statement} 执行计划为空。");
                    }

                    var planPath = Path.Combine(
                        planDirectory,
                        $"{statement}.{database.PlanFileExtension}");
                    await File.WriteAllTextAsync(planPath, content, cancellationToken);
                    planFiles.Add(Path.GetRelativePath(outputDirectory, planPath));
                }

                var statistics = AuditingQueryStatistics.Calculate(samples);
                var pageResult = lastResult
                    ?? throw new InvalidOperationException("采样未产生查询结果。");
                scenarioResults.Add(new AuditingQueryScenarioResult(
                    scenario.Name,
                    pageResult.TotalRows,
                    pageResult.ReturnedRows,
                    statistics,
                    samples.Select(sample => sample.TotalMilliseconds).ToArray(),
                    planFiles));
                Console.WriteLine(
                    $"[{providerName}] {scenario.Name}: "
                    + $"P50={statistics.P50Milliseconds:0.###} ms, "
                    + $"P95={statistics.P95Milliseconds:0.###} ms, "
                    + $"P99={statistics.P99Milliseconds:0.###} ms");
            }

            providerResults.Add(new AuditingQueryProviderResult(
                database.ProviderName,
                database.ContainerImage,
                await database.GetVersionAsync(cancellationToken),
                seedStopwatch.Elapsed,
                scenarioResults));
        }

        var report = AuditingQueryReportWriter.CreateReport(options, providerResults);
        await AuditingQueryReportWriter.WriteAsync(
            outputDirectory,
            report,
            cancellationToken);
        Console.WriteLine($"基准工件：{outputDirectory}");
    }
}
