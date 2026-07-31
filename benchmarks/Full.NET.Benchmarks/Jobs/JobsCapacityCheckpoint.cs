using System.Text.Json;

namespace Full.NET.Benchmarks.Jobs;

public sealed class JobsCapacityCheckpoint
{
    private JobsCapacityCheckpoint(
        IReadOnlyList<JobsCapacityProviderResult> providers)
    {
        Providers = providers;
    }

    public IReadOnlyList<JobsCapacityProviderResult> Providers { get; }

    public static async Task<JobsCapacityCheckpoint> LoadAsync(
        JobsCapacityOptions options,
        IReadOnlyList<JobsCapacityScenario> scenarios,
        string buildFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        var path = Path.Combine(options.OutputDirectory, "report.json");
        if (!options.ResumeEnabled || !File.Exists(path))
        {
            return new JobsCapacityCheckpoint([]);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var report = await JsonSerializer.DeserializeAsync<JobsCapacityReport>(
            stream,
            JobsCapacityReportWriter.CreateJsonOptions(
                writeIndented: false),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Jobs capacity checkpoint 不是有效报告。");
        if (!string.Equals(
                report.BuildFingerprint,
                buildFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jobs capacity checkpoint 构建指纹与当前执行程序集不一致。");
        }

        if (!OptionsMatch(options, report.Options)
            || !scenarios.SequenceEqual(report.Scenarios))
        {
            throw new InvalidOperationException(
                "Jobs capacity checkpoint 矩阵参数与当前运行不一致。");
        }

        var keys = report.Providers
            .SelectMany(provider => provider.Runs.Select(run => (
                Provider: provider.Provider.ToLowerInvariant(),
                run.Scenario,
                run.Repetition)))
            .ToArray();
        if (keys.Distinct().Count() != keys.Length
            || keys.Any(key =>
                !options.Providers.Contains(
                    key.Provider,
                    StringComparer.OrdinalIgnoreCase)
                || !scenarios.Contains(key.Scenario)
                || key.Repetition < 1
                || key.Repetition > options.Repetitions))
        {
            throw new InvalidOperationException(
                "Jobs capacity checkpoint 包含重复或越界完成键。");
        }

        return new JobsCapacityCheckpoint(report.Providers);
    }

    private static bool OptionsMatch(
        JobsCapacityOptions current,
        JobsCapacityOptions checkpoint) =>
        current.Providers.SequenceEqual(
            checkpoint.Providers,
            StringComparer.OrdinalIgnoreCase)
        && current.ConcurrencyLevels.SequenceEqual(
            checkpoint.ConcurrencyLevels)
        && current.HandlerDelayMilliseconds.SequenceEqual(
            checkpoint.HandlerDelayMilliseconds)
        && current.ReplicaCounts.SequenceEqual(
            checkpoint.ReplicaCounts)
        && current.Repetitions == checkpoint.Repetitions
        && current.Warmup == checkpoint.Warmup
        && current.Duration == checkpoint.Duration
        && current.SeedJobs == checkpoint.SeedJobs
        && current.BatchSize == checkpoint.BatchSize
        && current.HandlerKeyCount == checkpoint.HandlerKeyCount
        && current.FailingHandlerKeyCount
            == checkpoint.FailingHandlerKeyCount
        && current.Lease == checkpoint.Lease
        && current.LeaseRenewal == checkpoint.LeaseRenewal;
}
