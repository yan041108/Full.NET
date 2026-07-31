using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsCapacityProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    IReadOnlyList<JobsCapacityRunResult> Runs);

public sealed record JobsCapacityReport(
    Guid ReportId,
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string BuildFingerprint,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    JobsCapacityOptions Options,
    IReadOnlyList<JobsCapacityScenario> Scenarios,
    IReadOnlyList<JobsCapacityProviderResult> Providers,
    JobsCapacityAssessmentResult Assessment)
{
    public int ExpectedRunCount =>
        Options.Providers.Count
        * Scenarios.Count
        * Options.Repetitions;

    public int CompletedRunCount =>
        Providers.Sum(provider => provider.Runs.Count);

    public bool IsComplete => JobsCapacityAssessment.HasCompleteMatrix(
        Options,
        Scenarios,
        Providers.SelectMany(provider => provider.Runs).ToArray());
}

public static class JobsCapacityReportWriter
{
    public static async Task WriteAsync(
        JobsCapacityOptions options,
        IReadOnlyList<JobsCapacityScenario> scenarios,
        IReadOnlyList<JobsCapacityProviderResult> providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(providers);
        Directory.CreateDirectory(options.OutputDirectory);
        var runs = providers.SelectMany(provider => provider.Runs).ToArray();
        var report = new JobsCapacityReport(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            GetSourceVersion(),
            GetBuildFingerprint(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options,
            scenarios,
            providers,
            JobsCapacityAssessment.Assess(options, scenarios, runs));
        var json = JsonSerializer.Serialize(
            report,
            CreateJsonOptions(writeIndented: true));
        await WriteTextAtomicallyAsync(
            Path.Combine(options.OutputDirectory, "report.json"),
            json,
            cancellationToken);
        await WriteTextAtomicallyAsync(
            Path.Combine(options.OutputDirectory, "summary.md"),
            BuildMarkdown(report),
            cancellationToken);
    }

    public static string GetBuildFingerprint()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(location)
            || !File.Exists(location))
        {
            throw new InvalidOperationException(
                "无法读取 Jobs capacity 执行程序集以计算构建指纹。");
        }

        using var stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    internal static string GetSourceVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";

    internal static JsonSerializerOptions CreateJsonOptions(
        bool writeIndented) =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            Converters = { new JsonStringEnumConverter() },
        };

    private static string BuildMarkdown(JobsCapacityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Jobs 并发容量证据");
        builder.AppendLine();
        builder.AppendLine(
            $"进度：{report.CompletedRunCount}/{report.ExpectedRunCount}；"
            + $"状态：{(report.IsComplete ? "COMPLETE" : "PARTIAL")}。");
        builder.AppendLine(
            $"建议：`{report.Assessment.Recommendation}`。");
        builder.AppendLine(
            "生产 `Jobs:Worker:MaxConcurrency` 默认值保持 `1`；"
            + "本报告不会自动修改配置。");
        builder.AppendLine();
        builder.AppendLine(
            "| Provider | 场景 | 重复 | terminal/s | queue P95 ms | "
            + "续租 | 正确性 |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var run in report.Providers
                     .SelectMany(provider => provider.Runs))
        {
            builder.AppendLine(
                $"| {run.Provider} | {run.Scenario.Name} "
                + $"| {run.Repetition} | {run.TerminalsPerSecond:F2} "
                + $"| {run.QueueLatency?.P95Milliseconds:F2} "
                + $"| {run.LeaseRenewalExecutions} "
                + $"| {(run.CorrectnessGatePassed ? "PASS" : "FAIL")} |");
        }

        if (report.Assessment.Reasons.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## 保持并发 1 的原因");
            builder.AppendLine();
            foreach (var reason in report.Assessment.Reasons)
            {
                builder.AppendLine($"- {reason}");
            }
        }

        return builder.ToString();
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath =
            $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
