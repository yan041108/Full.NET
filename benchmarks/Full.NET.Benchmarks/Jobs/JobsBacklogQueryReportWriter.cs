using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsBacklogQueryProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    TimeSpan SeedDuration,
    JobsBacklogDatasetExpectation Expectation,
    JobsBacklogQueryResult QueryResult,
    JobsBacklogQueryStatistics Statistics,
    IReadOnlyList<double> SamplesMilliseconds,
    IReadOnlyList<string> PlanFiles);

public sealed record JobsBacklogQueryBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int Concurrency,
    DateTimeOffset ReferenceUtc,
    IReadOnlyList<JobsBacklogQueryProviderResult> Providers);

public static class JobsBacklogQueryReportWriter
{
    public static JobsBacklogQueryBenchmarkReport CreateReport(
        JobsBacklogQueryBenchmarkOptions options,
        IReadOnlyList<JobsBacklogQueryProviderResult> providers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);
        var sourceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        return new JobsBacklogQueryBenchmarkReport(
            DateTimeOffset.UtcNow,
            sourceVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options.Rows,
            options.WarmupIterations,
            options.MeasurementIterations,
            options.Concurrency,
            options.ReferenceUtc,
            providers);
    }

    public static async Task WriteAsync(
        string outputDirectory,
        JobsBacklogQueryBenchmarkReport report,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(outputDirectory);
        var jsonOptions =
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
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

    private static string BuildMarkdown(
        JobsBacklogQueryBenchmarkReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Full.NET Jobs 积压查询双库证据");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- 源版本：`{report.SourceVersion}`");
        builder.AppendLine($"- 运行时：`{report.Framework}`");
        builder.AppendLine($"- 操作系统：`{report.OperatingSystem}`");
        builder.AppendLine($"- 处理器逻辑核：`{report.ProcessorCount}`");
        builder.AppendLine(
            $"- 数据集：`{report.Rows}` 行；预热 "
            + $"`{report.WarmupIterations}`；采样 "
            + $"`{report.MeasurementIterations}`；并发 "
            + $"`{report.Concurrency}`");
        builder.AppendLine($"- 观测时间：`{report.ReferenceUtc:O}`");
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
            builder.AppendLine(
                $"- 数据库版本：`{provider.DatabaseVersion}`");
            builder.AppendLine(
                $"- 数据准备耗时：`{Format(
                    provider.SeedDuration.TotalSeconds)} s`");
            builder.AppendLine(
                $"- 正确性门禁：`{(
                    provider.QueryResult.Matches(provider.Expectation)
                        ? "PASS"
                        : "FAIL")}`");
            builder.AppendLine(
                $"- Host pending：期望 `{provider.Expectation.PendingCount}`，"
                + $"实际 `{provider.QueryResult.PendingCount}`");
            builder.AppendLine(
                $"- 到期重试：期望 `{provider.Expectation.DueRetryCount}`，"
                + $"实际 `{provider.QueryResult.DueRetryCount}`");
            builder.AppendLine(
                $"- 租户 pending 噪声："
                + $"`{provider.Expectation.TenantPendingNoiseCount}`");
            builder.AppendLine();
            builder.AppendLine(
                "| 样本 | P50 ms | P95 ms | P99 ms | Min ms | Max ms |");
            builder.AppendLine(
                "| ---: | ---: | ---: | ---: | ---: | ---: |");
            var stats = provider.Statistics;
            builder.AppendLine(
                $"| {stats.SampleCount} | "
                + $"{Format(stats.P50Milliseconds)} | "
                + $"{Format(stats.P95Milliseconds)} | "
                + $"{Format(stats.P99Milliseconds)} | "
                + $"{Format(stats.MinimumMilliseconds)} | "
                + $"{Format(stats.MaximumMilliseconds)} |");
            builder.AppendLine();
            builder.AppendLine("执行计划文件：");
            builder.AppendLine();
            foreach (var plan in provider.PlanFiles)
            {
                builder.AppendLine(
                    $"- `{plan.Replace('\\', '/')}`");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
