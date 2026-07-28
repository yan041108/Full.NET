using System.Diagnostics;

namespace Full.NET.Benchmarks.Auditing;

public static class AuditingCursorAbBenchmarkRunner
{
    private static readonly AuditingCursorQueryStrategy[] Strategies =
    [
        AuditingCursorQueryStrategy.OffsetEndpoint,
        AuditingCursorQueryStrategy.CursorEndpoint,
    ];

    public static async Task RunAsync(
        AuditingQueryBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Mode != AuditingQueryBenchmarkMode.CursorAb)
        {
            throw new ArgumentException(
                "游标 A/B Runner 只接受 cursor-ab 模式。",
                nameof(options));
        }

        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var providerResults = new List<AuditingCursorAbProviderResult>();
        var offset = options.Rows - options.PageSize;

        foreach (var providerName in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"[{providerName}] 启动游标 A/B 隔离容器并执行正式迁移...");
            await using var database = await AuditingBenchmarkDatabase.StartAsync(
                providerName,
                cancellationToken);
            var cursorDatabase = database as IAuditingCursorBenchmarkDatabase
                ?? throw new InvalidOperationException(
                    $"{providerName} 未实现游标 A/B 数据库边界。");

            Console.WriteLine($"[{providerName}] 写入 {options.Rows} 行确定性分布数据...");
            var seedStopwatch = Stopwatch.StartNew();
            await database.SeedAsync(
                options.Rows,
                options.ReferenceUtc,
                cancellationToken);
            seedStopwatch.Stop();
            var boundary = await cursorDatabase.FindDeepCursorBoundaryAsync(
                offset,
                cancellationToken);

            for (var index = 0; index < options.WarmupIterations; index++)
            {
                foreach (var strategy in Strategies)
                {
                    await cursorDatabase.ExecuteCursorComparisonAsync(
                        strategy,
                        boundary,
                        offset,
                        options.PageSize,
                        options.Rows,
                        cancellationToken);
                }
            }

            var samples = Strategies.ToDictionary(
                strategy => strategy,
                _ => new List<TimeSpan>(options.MeasurementIterations));
            var lastResults = new Dictionary<
                AuditingCursorQueryStrategy,
                AuditingQueryPageResult>();
            for (var index = 0; index < options.MeasurementIterations; index++)
            {
                var orderedStrategies = index % 2 == 0
                    ? Strategies
                    : Strategies.Reverse();
                foreach (var strategy in orderedStrategies)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var result = await cursorDatabase.ExecuteCursorComparisonAsync(
                        strategy,
                        boundary,
                        offset,
                        options.PageSize,
                        options.Rows,
                        cancellationToken);
                    stopwatch.Stop();
                    samples[strategy].Add(stopwatch.Elapsed);
                    lastResults[strategy] = result;
                }
            }

            if (lastResults[Strategies[0]] != lastResults[Strategies[1]])
            {
                throw new InvalidOperationException(
                    $"{providerName} 深 OFFSET 与游标返回的有序行不一致。");
            }

            var workloads = new List<AuditingCursorAbWorkloadResult>();
            foreach (var strategy in Strategies)
            {
                var strategyName = GetStrategyName(strategy);
                var planDirectory = Path.Combine(
                    outputDirectory,
                    providerName,
                    strategyName);
                Directory.CreateDirectory(planDirectory);
                var plans = await cursorDatabase.CaptureCursorComparisonPlansAsync(
                    strategy,
                    boundary,
                    offset,
                    options.PageSize,
                    cancellationToken);
                var planFiles = new List<string>();
                foreach (var (statement, content) in plans)
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException(
                            $"{providerName}/{strategyName}/{statement} 执行计划为空。");
                    }

                    var planPath = Path.Combine(
                        planDirectory,
                        $"{statement}.{database.PlanFileExtension}");
                    await File.WriteAllTextAsync(
                        planPath,
                        content,
                        cancellationToken);
                    planFiles.Add(Path.GetRelativePath(outputDirectory, planPath));
                }

                var statistics = AuditingQueryStatistics.Calculate(samples[strategy]);
                workloads.Add(new AuditingCursorAbWorkloadResult(
                    strategyName,
                    lastResults[strategy].TotalRows,
                    lastResults[strategy].ReturnedRows,
                    statistics,
                    samples[strategy]
                        .Select(sample => sample.TotalMilliseconds)
                        .ToArray(),
                    planFiles));
                Console.WriteLine(
                    $"[{providerName}] {strategyName}: "
                    + $"P50={statistics.P50Milliseconds:0.###} ms, "
                    + $"P95={statistics.P95Milliseconds:0.###} ms, "
                    + $"P99={statistics.P99Milliseconds:0.###} ms");
            }

            providerResults.Add(new AuditingCursorAbProviderResult(
                database.ProviderName,
                database.ContainerImage,
                await database.GetVersionAsync(cancellationToken),
                seedStopwatch.Elapsed,
                offset,
                workloads));
        }

        var report = AuditingCursorAbReportWriter.CreateReport(
            options,
            providerResults);
        await AuditingCursorAbReportWriter.WriteAsync(
            outputDirectory,
            report,
            cancellationToken);
        Console.WriteLine($"游标 A/B 工件：{outputDirectory}");
    }

    private static string GetStrategyName(AuditingCursorQueryStrategy strategy) =>
        strategy switch
        {
            AuditingCursorQueryStrategy.OffsetEndpoint => "offset_endpoint",
            AuditingCursorQueryStrategy.CursorEndpoint => "cursor_endpoint",
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };
}
