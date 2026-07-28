using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingCursorAbWorkloadResult(
    string Strategy,
    long TotalRows,
    int ReturnedRows,
    AuditingQueryStatistics Statistics,
    IReadOnlyList<double> SamplesMilliseconds,
    IReadOnlyList<string> PlanFiles);

public sealed record AuditingCursorAbProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    TimeSpan SeedDuration,
    int Offset,
    IReadOnlyList<AuditingCursorAbWorkloadResult> Workloads);

public sealed record AuditingCursorAbReport(
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int PageSize,
    DateTimeOffset ReferenceUtc,
    IReadOnlyList<AuditingCursorAbProviderResult> Providers);

public static class AuditingCursorAbReportWriter
{
    public static AuditingCursorAbReport CreateReport(
        AuditingQueryBenchmarkOptions options,
        IReadOnlyList<AuditingCursorAbProviderResult> providers) =>
        new(
            DateTimeOffset.UtcNow,
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options.Rows,
            options.WarmupIterations,
            options.MeasurementIterations,
            options.PageSize,
            options.ReferenceUtc,
            providers);

    public static async Task WriteAsync(
        string outputDirectory,
        AuditingCursorAbReport report,
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

    private static string BuildMarkdown(AuditingCursorAbReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Full.NET 访问日志深 OFFSET 与游标 A/B");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- 源版本：`{report.SourceVersion}`");
        builder.AppendLine($"- 运行时：`{report.Framework}`");
        builder.AppendLine($"- 操作系统：`{report.OperatingSystem}`");
        builder.AppendLine($"- 处理器逻辑核：`{report.ProcessorCount}`");
        builder.AppendLine(
            $"- 数据集：`{report.Rows}` 行；页面 `{report.PageSize}`；"
            + $"预热 `{report.WarmupIterations}`；采样 `{report.MeasurementIterations}`");
        builder.AppendLine();
        builder.AppendLine(
            "> OFFSET 策略模拟旧端点的 COUNT＋深页列表；cursor 策略模拟新端点的单次 "
            + "keyset 列表。两者响应语义不同，结果用于评价显式游标端点，"
            + "不能据此静默替换需要精确总数的调用。");
        builder.AppendLine();

        foreach (var provider in report.Providers)
        {
            builder.AppendLine($"## {provider.Provider}");
            builder.AppendLine();
            builder.AppendLine($"- 镜像：`{provider.ContainerImage}`");
            builder.AppendLine($"- 数据库版本：`{provider.DatabaseVersion}`");
            builder.AppendLine($"- 深页 OFFSET：`{provider.Offset}`");
            builder.AppendLine(
                $"- 数据准备耗时：`{Format(provider.SeedDuration.TotalSeconds)} s`");
            builder.AppendLine();
            builder.AppendLine(
                "| 策略 | 总行数 | 返回行数 | P50 ms | P95 ms | P99 ms | Min ms | Max ms |");
            builder.AppendLine(
                "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var workload in provider.Workloads)
            {
                var stats = workload.Statistics;
                builder.AppendLine(
                    $"| `{workload.Strategy}` | {workload.TotalRows} | "
                    + $"{workload.ReturnedRows} | {Format(stats.P50Milliseconds)} | "
                    + $"{Format(stats.P95Milliseconds)} | "
                    + $"{Format(stats.P99Milliseconds)} | "
                    + $"{Format(stats.MinimumMilliseconds)} | "
                    + $"{Format(stats.MaximumMilliseconds)} |");
            }

            builder.AppendLine();
            builder.AppendLine("执行计划文件：");
            builder.AppendLine();
            foreach (var plan in provider.Workloads.SelectMany(
                         workload => workload.PlanFiles))
            {
                builder.AppendLine($"- `{plan.Replace('\\', '/')}`");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
