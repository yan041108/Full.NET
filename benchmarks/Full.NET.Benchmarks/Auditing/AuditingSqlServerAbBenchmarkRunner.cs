using System.Diagnostics;

namespace Full.NET.Benchmarks.Auditing;

public static class AuditingSqlServerAbBenchmarkRunner
{
    public static async Task RunAsync(
        AuditingQueryBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Mode != AuditingQueryBenchmarkMode.SqlServerPlanAb)
        {
            throw new ArgumentException(
                "SQL Server A/B Runner 只接受 sqlserver-plan-ab 模式。",
                nameof(options));
        }

        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var scenarios = AuditingQueryScenarios.Create(options, options.ReferenceUtc);
        var sequences = AuditingSqlServerAbSequences.Create(scenarios);
        var workloads = new List<AuditingSqlServerAbWorkloadResult>();

        Console.WriteLine("[sqlserver] 启动隔离容器并执行正式迁移...");
        await using var database = await AuditingBenchmarkDatabase.StartAsync(
            "sqlserver",
            cancellationToken);
        var sqlServer = database as SqlServerAuditingBenchmarkDatabase
            ?? throw new InvalidOperationException("A/B Runner 未获得 SQL Server 数据库。");

        Console.WriteLine($"[sqlserver] 写入 {options.Rows} 行确定性分布数据...");
        var seedStopwatch = Stopwatch.StartNew();
        await sqlServer.SeedAsync(
            options.Rows,
            options.ReferenceUtc,
            cancellationToken);
        seedStopwatch.Stop();

        foreach (var strategy in AuditingSqlServerQueryFactory.Strategies)
        {
            var strategyName = AuditingSqlServerQueryFactory.GetName(strategy);
            foreach (var sequence in sequences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine(
                    $"[sqlserver] {strategyName}/{sequence.Name}: 清空隔离计划缓存...");
                await sqlServer.ClearPlanCacheAsync(cancellationToken);

                for (var index = 0; index < options.WarmupIterations; index++)
                {
                    foreach (var scenario in sequence.Scenarios)
                    {
                        await sqlServer.ExecuteSqlServerPageAsync(
                            strategy,
                            scenario,
                            cancellationToken);
                    }
                }

                var samples = sequence.Scenarios.ToDictionary(
                    scenario => scenario.Name,
                    _ => new List<TimeSpan>(options.MeasurementIterations),
                    StringComparer.Ordinal);
                var lastResults = new Dictionary<string, AuditingQueryPageResult>(
                    StringComparer.Ordinal);
                for (var index = 0; index < options.MeasurementIterations; index++)
                {
                    foreach (var scenario in sequence.Scenarios)
                    {
                        var stopwatch = Stopwatch.StartNew();
                        var pageResult = await sqlServer.ExecuteSqlServerPageAsync(
                            strategy,
                            scenario,
                            cancellationToken);
                        stopwatch.Stop();
                        samples[scenario.Name].Add(stopwatch.Elapsed);
                        lastResults[scenario.Name] = pageResult;
                    }
                }

                for (var orderPosition = 0;
                     orderPosition < sequence.Scenarios.Count;
                     orderPosition++)
                {
                    var scenario = sequence.Scenarios[orderPosition];
                    var planDirectory = Path.Combine(
                        outputDirectory,
                        strategyName,
                        sequence.Name,
                        scenario.Name);
                    Directory.CreateDirectory(planDirectory);
                    var plans = await sqlServer.CaptureSqlServerPlansAsync(
                        strategy,
                        scenario,
                        cancellationToken);
                    var planResults = new List<AuditingSqlServerAbPlanResult>();
                    foreach (var (statement, content) in plans)
                    {
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            throw new InvalidOperationException(
                                $"{strategyName}/{sequence.Name}/{scenario.Name}/"
                                + $"{statement} 执行计划为空。");
                        }

                        var planPath = Path.Combine(
                            planDirectory,
                            $"{statement}.showplan.xml");
                        await File.WriteAllTextAsync(
                            planPath,
                            content,
                            cancellationToken);
                        planResults.Add(new AuditingSqlServerAbPlanResult(
                            statement,
                            Path.GetRelativePath(outputDirectory, planPath),
                            AuditingSqlServerPlanMetrics.Parse(content)));
                    }

                    var statistics = AuditingQueryStatistics.Calculate(
                        samples[scenario.Name]);
                    var pageResult = lastResults[scenario.Name];
                    workloads.Add(new AuditingSqlServerAbWorkloadResult(
                        strategyName,
                        sequence.Name,
                        scenario.Name,
                        orderPosition + 1,
                        pageResult.TotalRows,
                        pageResult.ReturnedRows,
                        statistics,
                        samples[scenario.Name]
                            .Select(sample => sample.TotalMilliseconds)
                            .ToArray(),
                        planResults));
                    Console.WriteLine(
                        $"[sqlserver] {strategyName}/{sequence.Name}/{scenario.Name}: "
                        + $"P50={statistics.P50Milliseconds:0.###} ms, "
                        + $"P95={statistics.P95Milliseconds:0.###} ms, "
                        + $"P99={statistics.P99Milliseconds:0.###} ms");
                }
            }
        }

        var report = AuditingSqlServerAbReportWriter.CreateReport(
            options,
            await sqlServer.GetVersionAsync(cancellationToken),
            seedStopwatch.Elapsed,
            workloads);
        await AuditingSqlServerAbReportWriter.WriteAsync(
            outputDirectory,
            report,
            cancellationToken);
        Console.WriteLine($"A/B 工件：{outputDirectory}");
    }
}
