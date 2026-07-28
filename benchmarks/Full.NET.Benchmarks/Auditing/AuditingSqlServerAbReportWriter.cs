using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingSqlServerAbPlanResult(
    string Statement,
    string PlanFile,
    AuditingSqlServerPlanMetrics Metrics);

public sealed record AuditingSqlServerAbWorkloadResult(
    string Strategy,
    string Sequence,
    string Scenario,
    int OrderPosition,
    long TotalRows,
    int ReturnedRows,
    AuditingQueryStatistics Statistics,
    IReadOnlyList<double> SamplesMilliseconds,
    IReadOnlyList<AuditingSqlServerAbPlanResult> Plans);

public sealed record AuditingSqlServerAbReport(
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    string ContainerImage,
    string DatabaseVersion,
    TimeSpan SeedDuration,
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int PageSize,
    int Concurrency,
    DateTimeOffset ReferenceUtc,
    IReadOnlyList<AuditingSqlServerAbWorkloadResult> Workloads);

public static class AuditingSqlServerAbReportWriter
{
    public static AuditingSqlServerAbReport CreateReport(
        AuditingQueryBenchmarkOptions options,
        string databaseVersion,
        TimeSpan seedDuration,
        IReadOnlyList<AuditingSqlServerAbWorkloadResult> workloads)
    {
        var sourceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        return new AuditingSqlServerAbReport(
            DateTimeOffset.UtcNow,
            sourceVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            SqlServerAuditingBenchmarkDatabase.Image,
            databaseVersion,
            seedDuration,
            options.Rows,
            options.WarmupIterations,
            options.MeasurementIterations,
            options.PageSize,
            options.Concurrency,
            options.ReferenceUtc,
            workloads);
    }

    public static async Task WriteAsync(
        string outputDirectory,
        AuditingSqlServerAbReport report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }),
            Encoding.UTF8,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "README.md"),
            BuildMarkdown(report),
            Encoding.UTF8,
            cancellationToken);
    }

    private static string BuildMarkdown(AuditingSqlServerAbReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Full.NET SQL Server 审计查询计划稳定性 A/B");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- 源版本：`{report.SourceVersion}`");
        builder.AppendLine($"- 运行时：`{report.Framework}`");
        builder.AppendLine($"- 操作系统：`{report.OperatingSystem}`");
        builder.AppendLine($"- 处理器逻辑核：`{report.ProcessorCount}`");
        builder.AppendLine($"- 镜像：`{report.ContainerImage}`");
        builder.AppendLine($"- 数据库版本：`{report.DatabaseVersion}`");
        builder.AppendLine(
            $"- 数据集：`{report.Rows}` 行；页面 `{report.PageSize}`；"
            + $"预热 `{report.WarmupIterations}`；采样 `{report.MeasurementIterations}`；"
            + $"并发 `{report.Concurrency}`");
        builder.AppendLine(
            $"- 数据准备耗时：`{Format(report.SeedDuration.TotalSeconds)} s`");
        builder.AppendLine();
        builder.AppendLine(
            "> 每个策略/顺序组合先清空隔离容器的计划缓存。缓存策略的 CompileCPU "
            + "表示该缓存计划的一次编译成本；recompile 表示本次计划采集的单次编译成本，"
            + "不能直接当作整个采样窗口的总编译 CPU。");
        builder.AppendLine();
        builder.AppendLine(
            "| 策略 | 顺序 | 场景 | 位置 | P50 ms | P95 ms | P99 ms | "
            + "Count 编译 CPU ms | List 编译 CPU ms | Count 逻辑读 | "
            + "List 逻辑读 | Count 读行 | List 读行 |");
        builder.AppendLine(
            "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | "
            + "---: | ---: | ---: | ---: |");
        foreach (var workload in report.Workloads)
        {
            var count = workload.Plans.Single(plan => plan.Statement == "count").Metrics;
            var list = workload.Plans.Single(plan => plan.Statement == "list").Metrics;
            builder.AppendLine(
                $"| `{workload.Strategy}` | `{workload.Sequence}` | "
                + $"`{workload.Scenario}` | {workload.OrderPosition} | "
                + $"{Format(workload.Statistics.P50Milliseconds)} | "
                + $"{Format(workload.Statistics.P95Milliseconds)} | "
                + $"{Format(workload.Statistics.P99Milliseconds)} | "
                + $"{count.CompileCpuMilliseconds} | {list.CompileCpuMilliseconds} | "
                + $"{count.ActualLogicalReads} | {list.ActualLogicalReads} | "
                + $"{count.ActualRowsRead} | {list.ActualRowsRead} |");
        }

        builder.AppendLine();
        builder.AppendLine("执行计划文件：");
        builder.AppendLine();
        foreach (var plan in report.Workloads.SelectMany(workload => workload.Plans))
        {
            builder.AppendLine($"- `{plan.PlanFile.Replace('\\', '/')}`");
        }

        return builder.ToString();
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
