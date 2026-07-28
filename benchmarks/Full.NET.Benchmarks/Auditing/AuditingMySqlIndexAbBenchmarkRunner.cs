using System.Diagnostics;

namespace Full.NET.Benchmarks.Auditing;

public static class AuditingMySqlIndexAbBenchmarkRunner
{
    public static async Task RunAsync(
        AuditingQueryBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Mode is not (
            AuditingQueryBenchmarkMode.MySqlIndexAb
            or AuditingQueryBenchmarkMode.MySqlLateMaterializationAb))
        {
            throw new ArgumentException(
                "MySQL A/B Runner 只接受已定义的 MySQL A/B 模式。",
                nameof(options));
        }

        var strategies = AuditingMySqlQueryFactory.GetStrategies(options.Mode);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var scenarios = AuditingQueryScenarios.Create(options, options.ReferenceUtc);
        var workloads = new List<AuditingMySqlIndexAbWorkloadResult>();

        Console.WriteLine("[mysql] 启动隔离容器并执行正式迁移...");
        await using var database = await AuditingBenchmarkDatabase.StartAsync(
            "mysql",
            cancellationToken);
        var mySql = database as MySqlAuditingBenchmarkDatabase
            ?? throw new InvalidOperationException("A/B Runner 未获得 MySQL 数据库。");

        Console.WriteLine($"[mysql] 写入 {options.Rows} 行确定性分布数据...");
        var seedStopwatch = Stopwatch.StartNew();
        await mySql.SeedAsync(
            options.Rows,
            options.ReferenceUtc,
            cancellationToken);
        seedStopwatch.Stop();

        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine(
                $"[mysql] {scenario.Name}: "
                + $"双策略预热 {options.WarmupIterations}，"
                + $"交替采样 {options.MeasurementIterations}...");
            for (var index = 0; index < options.WarmupIterations; index++)
            {
                foreach (var strategy in strategies)
                {
                    await mySql.ExecuteMySqlPageAsync(
                        strategy,
                        scenario,
                        cancellationToken);
                }
            }

            var samples = strategies.ToDictionary(
                strategy => strategy,
                _ => new List<TimeSpan>(options.MeasurementIterations));
            var lastResults = new Dictionary<
                AuditingMySqlQueryStrategy,
                AuditingQueryPageResult>();
            foreach (var strategy in AuditingMySqlIndexAbSampling.CreateStrategyOrder(
                         options.MeasurementIterations,
                         strategies))
            {
                var stopwatch = Stopwatch.StartNew();
                var pageResult = await mySql.ExecuteMySqlPageAsync(
                    strategy,
                    scenario,
                    cancellationToken);
                stopwatch.Stop();
                samples[strategy].Add(stopwatch.Elapsed);
                lastResults[strategy] = pageResult;
            }

            var currentResult = lastResults[strategies[0]];
            var candidateResult = lastResults[strategies[1]];
            if (currentResult != candidateResult)
            {
                throw new InvalidOperationException(
                    $"MySQL A/B {scenario.Name} 的总数、返回行数或有序行标识不一致。");
            }

            foreach (var strategy in strategies)
            {
                var strategyName = AuditingMySqlQueryFactory.GetName(strategy);
                var strategySamples = samples[strategy];
                if (strategySamples.Count != options.MeasurementIterations)
                {
                    throw new InvalidOperationException(
                        $"{strategyName}/{scenario.Name} 样本数不完整。");
                }

                var planDirectory = Path.Combine(
                    outputDirectory,
                    strategyName,
                    scenario.Name);
                Directory.CreateDirectory(planDirectory);
                var plans = await mySql.CaptureMySqlPlansAsync(
                    strategy,
                    scenario,
                    cancellationToken);
                var planResults = new List<AuditingMySqlIndexAbPlanResult>();
                foreach (var (statement, content) in plans)
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException(
                            $"{strategyName}/{scenario.Name}/{statement} 执行计划为空。");
                    }

                    var planPath = Path.Combine(
                        planDirectory,
                        $"{statement}.explain.json");
                    await File.WriteAllTextAsync(
                        planPath,
                        content,
                        cancellationToken);
                    planResults.Add(new AuditingMySqlIndexAbPlanResult(
                        statement,
                        Path.GetRelativePath(outputDirectory, planPath)));
                }

                var statistics = AuditingQueryStatistics.Calculate(strategySamples);
                workloads.Add(new AuditingMySqlIndexAbWorkloadResult(
                    strategyName,
                    scenario.Name,
                    currentResult.TotalRows,
                    currentResult.ReturnedRows,
                    statistics,
                    strategySamples
                        .Select(sample => sample.TotalMilliseconds)
                        .ToArray(),
                    planResults));
                Console.WriteLine(
                    $"[mysql] {strategyName}/{scenario.Name}: "
                    + $"P50={statistics.P50Milliseconds:0.###} ms, "
                    + $"P95={statistics.P95Milliseconds:0.###} ms, "
                    + $"P99={statistics.P99Milliseconds:0.###} ms");
            }
        }

        if (workloads.Count != scenarios.Count
            * strategies.Count)
        {
            throw new InvalidOperationException("MySQL A/B workload 数量不完整。");
        }

        var report = AuditingMySqlIndexAbReportWriter.CreateReport(
            options,
            await mySql.GetVersionAsync(cancellationToken),
            seedStopwatch.Elapsed,
            workloads);
        await AuditingMySqlIndexAbReportWriter.WriteAsync(
            outputDirectory,
            report,
            cancellationToken);
        Console.WriteLine($"MySQL A/B 工件：{outputDirectory}");
    }
}

public static class AuditingMySqlIndexAbSampling
{
    public static IReadOnlyList<AuditingMySqlQueryStrategy> CreateStrategyOrder(
        int measurementIterations) =>
        CreateStrategyOrder(
            measurementIterations,
            AuditingMySqlQueryFactory.Strategies);

    public static IReadOnlyList<AuditingMySqlQueryStrategy> CreateStrategyOrder(
        int measurementIterations,
        IReadOnlyList<AuditingMySqlQueryStrategy> strategies)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measurementIterations);
        ArgumentNullException.ThrowIfNull(strategies);
        if (strategies.Count != 2 || strategies[0] == strategies[1])
        {
            throw new ArgumentException(
                "MySQL A/B 配对采样必须提供两个不同策略。",
                nameof(strategies));
        }

        var order = new AuditingMySqlQueryStrategy[measurementIterations * 2];
        for (var index = 0; index < measurementIterations; index++)
        {
            var first = index % 2 == 0
                ? strategies[0]
                : strategies[1];
            order[index * 2] = first;
            order[(index * 2) + 1] = first == strategies[0]
                ? strategies[1]
                : strategies[0];
        }

        return order;
    }
}
