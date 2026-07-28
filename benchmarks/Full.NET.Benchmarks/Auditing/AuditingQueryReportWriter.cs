using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingQueryScenarioResult(
    string Name,
    long TotalRows,
    int ReturnedRows,
    AuditingQueryStatistics Statistics,
    IReadOnlyList<double> SamplesMilliseconds,
    IReadOnlyList<string> PlanFiles);

public sealed record AuditingQueryProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    TimeSpan SeedDuration,
    IReadOnlyList<AuditingQueryScenarioResult> Scenarios);

public sealed record AuditingQueryBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int PageSize,
    int Concurrency,
    DateTimeOffset ReferenceUtc,
    IReadOnlyList<AuditingQueryProviderResult> Providers);

public static class AuditingQueryReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        AuditingQueryBenchmarkReport report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            Encoding.UTF8,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "README.md"),
            BuildMarkdown(report),
            Encoding.UTF8,
            cancellationToken);
    }

    public static AuditingQueryBenchmarkReport CreateReport(
        AuditingQueryBenchmarkOptions options,
        IReadOnlyList<AuditingQueryProviderResult> providers)
    {
        var sourceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        return new AuditingQueryBenchmarkReport(
            DateTimeOffset.UtcNow,
            sourceVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options.Rows,
            options.WarmupIterations,
            options.MeasurementIterations,
            options.PageSize,
            options.Concurrency,
            options.ReferenceUtc,
            providers);
    }

    private static string BuildMarkdown(AuditingQueryBenchmarkReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Full.NET 审计查询双库基准");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- 源版本：`{report.SourceVersion}`");
        builder.AppendLine($"- 运行时：`{report.Framework}`");
        builder.AppendLine($"- 操作系统：`{report.OperatingSystem}`");
        builder.AppendLine($"- 处理器逻辑核：`{report.ProcessorCount}`");
        builder.AppendLine(
            $"- 数据集：`{report.Rows}` 行；页面 `{report.PageSize}`；"
            + $"预热 `{report.WarmupIterations}`；采样 `{report.MeasurementIterations}`；"
            + $"并发 `{report.Concurrency}`");
        builder.AppendLine($"- 数据集结束时间：`{report.ReferenceUtc:O}`");
        builder.AppendLine();
        builder.AppendLine(
            "> 该结果是本机 Testcontainers 受控实验，不代表生产 SLA；"
            + "不同 Provider 的绝对耗时不得直接横向排名。");
        builder.AppendLine();

        foreach (var provider in report.Providers)
        {
            builder.AppendLine($"## {provider.Provider}");
            builder.AppendLine();
            builder.AppendLine($"- 镜像：`{provider.ContainerImage}`");
            builder.AppendLine($"- 数据库版本：`{provider.DatabaseVersion}`");
            builder.AppendLine(
                $"- 数据准备耗时：`{Format(provider.SeedDuration.TotalSeconds)} s`");
            builder.AppendLine();
            builder.AppendLine(
                "| 场景 | 总行数 | 返回行数 | P50 ms | P95 ms | P99 ms | Min ms | Max ms |");
            builder.AppendLine(
                "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var scenario in provider.Scenarios)
            {
                var stats = scenario.Statistics;
                builder.AppendLine(
                    $"| `{scenario.Name}` | {scenario.TotalRows} | {scenario.ReturnedRows} | "
                    + $"{Format(stats.P50Milliseconds)} | {Format(stats.P95Milliseconds)} | "
                    + $"{Format(stats.P99Milliseconds)} | {Format(stats.MinimumMilliseconds)} | "
                    + $"{Format(stats.MaximumMilliseconds)} |");
            }

            builder.AppendLine();
            builder.AppendLine("执行计划文件：");
            builder.AppendLine();
            foreach (var plan in provider.Scenarios.SelectMany(scenario => scenario.PlanFiles))
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
