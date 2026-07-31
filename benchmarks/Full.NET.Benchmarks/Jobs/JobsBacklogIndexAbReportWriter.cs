using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsBacklogMutationStatistics(
    JobsBacklogQueryStatistics TriggerInsert,
    JobsBacklogQueryStatistics Claim,
    JobsBacklogQueryStatistics TerminalSuccess);

public sealed record JobsBacklogIndexVariantResult(
    JobsBacklogIndexVariant Variant,
    JobsBacklogQueryResult QueryResult,
    JobsBacklogQueryStatistics QueryStatistics,
    IReadOnlyList<double> QuerySamplesMilliseconds,
    JobsBacklogMutationStatistics Mutations,
    IReadOnlyList<string> PlanFiles,
    bool UsesCandidateIndex);

public sealed record JobsBacklogIndexAbAssessmentResult(
    bool MigrationAllowed,
    IReadOnlyList<string> Reasons);

public sealed record JobsBacklogIndexProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    TimeSpan SeedDuration,
    TimeSpan CandidateIndexBuildDuration,
    long CandidateIndexSizeBytes,
    JobsBacklogDatasetExpectation Expectation,
    JobsBacklogIndexVariantResult Baseline,
    JobsBacklogIndexVariantResult Candidate,
    JobsBacklogIndexAbAssessmentResult Assessment);

public sealed record JobsBacklogIndexAbReport(
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Experiment,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int MutationIterations,
    int Concurrency,
    DateTimeOffset ReferenceUtc,
    bool MigrationAllowed,
    IReadOnlyList<JobsBacklogIndexProviderResult> Providers);

public static class JobsBacklogIndexAbAssessment
{
    private const double MaximumWriteRegressionRatio = 1.2d;

    public static JobsBacklogIndexAbAssessmentResult Assess(
        JobsBacklogDatasetExpectation expectation,
        JobsBacklogIndexVariantResult baseline,
        JobsBacklogIndexVariantResult candidate)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        var reasons = new List<string>();

        if (!baseline.QueryResult.Matches(expectation)
            || !candidate.QueryResult.Matches(expectation))
        {
            reasons.Add("baseline 或 candidate 未通过 backlog 正确性门禁。");
        }

        if (baseline.PlanFiles.Count == 0
            || candidate.PlanFiles.Count == 0)
        {
            reasons.Add("baseline 或 candidate 缺少执行计划工件。");
        }

        if (baseline.UsesCandidateIndex)
        {
            reasons.Add("baseline 计划不应采用候选索引。");
        }

        if (!candidate.UsesCandidateIndex)
        {
            reasons.Add("candidate 计划未采用候选索引。");
        }

        if (candidate.QueryStatistics.P95Milliseconds
                >= baseline.QueryStatistics.P95Milliseconds
            || candidate.QueryStatistics.P99Milliseconds
                >= baseline.QueryStatistics.P99Milliseconds)
        {
            reasons.Add("candidate 查询 P95/P99 未同时严格改善。");
        }

        AddWriteRegressionReason(
            reasons,
            "trigger_insert",
            baseline.Mutations.TriggerInsert,
            candidate.Mutations.TriggerInsert);
        AddWriteRegressionReason(
            reasons,
            "claim",
            baseline.Mutations.Claim,
            candidate.Mutations.Claim);
        AddWriteRegressionReason(
            reasons,
            "terminal_success",
            baseline.Mutations.TerminalSuccess,
            candidate.Mutations.TerminalSuccess);

        return new JobsBacklogIndexAbAssessmentResult(
            reasons.Count == 0,
            reasons);
    }

    private static void AddWriteRegressionReason(
        ICollection<string> reasons,
        string workload,
        JobsBacklogQueryStatistics baseline,
        JobsBacklogQueryStatistics candidate)
    {
        var maximum = baseline.P95Milliseconds
            * MaximumWriteRegressionRatio;
        if (candidate.P95Milliseconds > maximum)
        {
            reasons.Add(
                $"{workload} P95 回归超过 20% 门槛："
                + $"baseline={baseline.P95Milliseconds:0.###} ms，"
                + $"candidate={candidate.P95Milliseconds:0.###} ms。");
        }
    }
}

public static class JobsBacklogIndexAbReportWriter
{
    public static JobsBacklogIndexAbReport CreateReport(
        JobsBacklogQueryBenchmarkOptions options,
        IReadOnlyList<JobsBacklogIndexProviderResult> providers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);
        if (options.Mode != JobsBacklogQueryBenchmarkMode.IndexAb)
        {
            throw new ArgumentException(
                "Jobs backlog index A/B 报告只接受 index-ab 模式。",
                nameof(options));
        }

        var sourceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        return new JobsBacklogIndexAbReport(
            DateTimeOffset.UtcNow,
            sourceVersion,
            "index-ab",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options.Rows,
            options.WarmupIterations,
            options.MeasurementIterations,
            options.MutationIterations,
            options.Concurrency,
            options.ReferenceUtc,
            providers.Count > 0
                && providers.All(provider =>
                    provider.Assessment.MigrationAllowed),
            providers);
    }

    public static async Task WriteAsync(
        string outputDirectory,
        JobsBacklogIndexAbReport report,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(report);
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

    private static string BuildMarkdown(
        JobsBacklogIndexAbReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Full.NET Jobs backlog index-ab");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- 源版本：`{report.SourceVersion}`");
        builder.AppendLine($"- 运行时：`{report.Framework}`");
        builder.AppendLine($"- 操作系统：`{report.OperatingSystem}`");
        builder.AppendLine($"- 处理器逻辑核：`{report.ProcessorCount}`");
        builder.AppendLine(
            $"- 数据集：`{report.Rows}` 行；预热 "
            + $"`{report.WarmupIterations}`；查询采样 "
            + $"`{report.MeasurementIterations}`；写路径采样 "
            + $"`{report.MutationIterations}`；并发 "
            + $"`{report.Concurrency}`");
        builder.AppendLine();
        builder.AppendLine(
            "> 结果来自本机 Testcontainers 受控实验，不代表生产 SLA；"
            + "不同 Provider 的绝对耗时不得横向排名。");
        builder.AppendLine();

        foreach (var provider in report.Providers)
        {
            builder.AppendLine($"## {provider.Provider}");
            builder.AppendLine();
            builder.AppendLine($"- 镜像：`{provider.ContainerImage}`");
            builder.AppendLine($"- 数据库版本：`{provider.DatabaseVersion}`");
            builder.AppendLine(
                $"- 数据准备：`{Format(
                    provider.SeedDuration.TotalSeconds)} s`");
            builder.AppendLine(
                $"- candidate 创建：`{Format(
                    provider.CandidateIndexBuildDuration.TotalMilliseconds)} ms`");
            builder.AppendLine(
                $"- candidate 索引体积：`{provider.CandidateIndexSizeBytes}` bytes");
            builder.AppendLine(
                "- candidate 计划采用候选索引："
                + $"`{(provider.Candidate.UsesCandidateIndex ? "是" : "否")}`");
            builder.AppendLine();
            builder.AppendLine(
                "| variant | workload | P50 ms | P95 ms | P99 ms |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: |");
            AppendVariant(builder, provider.Baseline);
            AppendVariant(builder, provider.Candidate);
            builder.AppendLine();
            builder.AppendLine(
                provider.Assessment.MigrationAllowed
                    ? "**门禁：允许进入独立迁移切片。**"
                    : "**门禁：不允许进入迁移切片。**");
            foreach (var reason in provider.Assessment.Reasons)
            {
                builder.AppendLine($"- {reason}");
            }

            builder.AppendLine();
            builder.AppendLine("执行计划文件：");
            builder.AppendLine();
            foreach (var plan in provider.Baseline.PlanFiles
                         .Concat(provider.Candidate.PlanFiles))
            {
                builder.AppendLine($"- `{plan.Replace('\\', '/')}`");
            }

            builder.AppendLine();
        }

        builder.AppendLine(
            report.MigrationAllowed
                ? "总体结论：允许进入独立迁移切片。"
                : "总体结论：不允许进入迁移切片。");
        return builder.ToString();
    }

    private static void AppendVariant(
        StringBuilder builder,
        JobsBacklogIndexVariantResult variant)
    {
        AppendStatistics(
            builder,
            variant.Variant,
            "backlog_query",
            variant.QueryStatistics);
        AppendStatistics(
            builder,
            variant.Variant,
            "trigger_insert",
            variant.Mutations.TriggerInsert);
        AppendStatistics(
            builder,
            variant.Variant,
            "claim",
            variant.Mutations.Claim);
        AppendStatistics(
            builder,
            variant.Variant,
            "terminal_success",
            variant.Mutations.TerminalSuccess);
    }

    private static void AppendStatistics(
        StringBuilder builder,
        JobsBacklogIndexVariant variant,
        string workload,
        JobsBacklogQueryStatistics statistics)
    {
        builder.AppendLine(
            $"| `{variant.ToString().ToLowerInvariant()}` | "
            + $"`{workload}` | "
            + $"{Format(statistics.P50Milliseconds)} | "
            + $"{Format(statistics.P95Milliseconds)} | "
            + $"{Format(statistics.P99Milliseconds)} |");
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
