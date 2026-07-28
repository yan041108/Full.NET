using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingMySqlIndexAbPlanResult(
    string Statement,
    string PlanFile);

public sealed record AuditingMySqlIndexAbWorkloadResult(
    string Strategy,
    string Scenario,
    long TotalRows,
    int ReturnedRows,
    AuditingQueryStatistics Statistics,
    IReadOnlyList<double> SamplesMilliseconds,
    IReadOnlyList<AuditingMySqlIndexAbPlanResult> Plans);

public sealed record AuditingMySqlIndexAbReport(
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Experiment,
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
    IReadOnlyList<AuditingMySqlIndexAbWorkloadResult> Workloads);

public static class AuditingMySqlIndexAbReportWriter
{
    public static AuditingMySqlIndexAbReport CreateReport(
        AuditingQueryBenchmarkOptions options,
        string databaseVersion,
        TimeSpan seedDuration,
        IReadOnlyList<AuditingMySqlIndexAbWorkloadResult> workloads)
    {
        var sourceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        return new AuditingMySqlIndexAbReport(
            DateTimeOffset.UtcNow,
            sourceVersion,
            GetExperimentName(options.Mode),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            MySqlAuditingBenchmarkDatabase.Image,
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
        AuditingMySqlIndexAbReport report,
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

    private static string BuildMarkdown(AuditingMySqlIndexAbReport report)
    {
        var builder = new StringBuilder();
        var title = report.Experiment == "late_materialization"
            ? "# Full.NET MySQL 审计深分页延迟物化 A/B"
            : "# Full.NET MySQL 审计深分页索引 Hint A/B";
        builder.AppendLine(title);
        builder.AppendLine();
        builder.AppendLine($"- 实验：`{report.Experiment}`");
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
            "> 两个策略在每个场景内成对执行并逐轮反转先后顺序。"
            + "该结果是本机 Testcontainers 受控实验，不代表生产 SLA。");
        builder.AppendLine();
        builder.AppendLine(
            "| 策略 | 场景 | 总行数 | 返回行数 | P50 ms | P95 ms | P99 ms |");
        builder.AppendLine(
            "| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var workload in report.Workloads)
        {
            builder.AppendLine(
                $"| `{workload.Strategy}` | `{workload.Scenario}` | "
                + $"{workload.TotalRows} | {workload.ReturnedRows} | "
                + $"{Format(workload.Statistics.P50Milliseconds)} | "
                + $"{Format(workload.Statistics.P95Milliseconds)} | "
                + $"{Format(workload.Statistics.P99Milliseconds)} |");
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

    private static string GetExperimentName(AuditingQueryBenchmarkMode mode) =>
        mode switch
        {
            AuditingQueryBenchmarkMode.MySqlIndexAb => "index_hint",
            AuditingQueryBenchmarkMode.MySqlLateMaterializationAb =>
                "late_materialization",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "不支持的 MySQL A/B 报告模式。"),
        };
}
