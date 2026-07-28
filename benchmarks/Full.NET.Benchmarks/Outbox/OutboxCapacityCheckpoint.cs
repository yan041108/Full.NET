using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.Outbox;

/// <summary>
/// 表示可从原子报告恢复的 Outbox 容量矩阵进度。
/// </summary>
/// <remarks>
/// Checkpoint 只接受同一构建版本和同一矩阵参数，避免跨版本或跨场景混合性能样本。
/// </remarks>
public sealed class OutboxCapacityCheckpoint
{
    private readonly OutboxCapacityOptions _options;
    private readonly IReadOnlyList<OutboxCapacityScenario> _scenarios;

    private OutboxCapacityCheckpoint(
        OutboxCapacityOptions options,
        IReadOnlyList<OutboxCapacityScenario> scenarios,
        IReadOnlyList<OutboxCapacityProviderResult> providers)
    {
        _options = options;
        _scenarios = scenarios;
        Providers = providers;
    }

    /// <summary>
    /// 获取已持久化的 Provider 结果。
    /// </summary>
    public IReadOnlyList<OutboxCapacityProviderResult> Providers { get; }

    /// <summary>
    /// 获取已完成的普通容量采样数量。
    /// </summary>
    public int CompletedRunCount =>
        Providers.Sum(provider => provider.Runs.Count);

    /// <summary>
    /// 获取当前矩阵仍待执行的普通容量采样数量。
    /// </summary>
    public int PendingRunCount =>
        checked(
            _options.Providers.Count
            * _scenarios.Count
            * _options.Repetitions)
        - CompletedRunCount;

    /// <summary>
    /// 判断指定 Provider、场景和重复轮次是否已完成。
    /// </summary>
    public bool HasRun(
        string provider,
        OutboxCapacityScenario scenario,
        int repetition) =>
        Providers
            .Where(result => ProviderEquals(result.Provider, provider))
            .SelectMany(result => result.Runs)
            .Any(run =>
                run.Scenario == scenario
                && run.Repetition == repetition);

    /// <summary>
    /// 判断指定 Provider 的遗弃租约恢复轮次是否已完成。
    /// </summary>
    public bool HasRecovery(string provider, int repetition) =>
        Providers
            .Where(result => ProviderEquals(result.Provider, provider))
            .SelectMany(result => result.Recoveries)
            .Any(recovery => recovery.Repetition == repetition);

    /// <summary>
    /// 从当前输出目录读取并验证 checkpoint；未启用恢复或报告不存在时返回空进度。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 报告损坏，或源版本、矩阵参数、完成键与当前运行不兼容。
    /// </exception>
    public static async Task<OutboxCapacityCheckpoint> LoadAsync(
        OutboxCapacityOptions options,
        IReadOnlyList<OutboxCapacityScenario> scenarios,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scenarios);
        var reportPath = Path.Combine(options.OutputDirectory, "report.json");
        if (!options.ResumeEnabled || !File.Exists(reportPath))
        {
            return new OutboxCapacityCheckpoint(options, scenarios, []);
        }

        await using var stream = new FileStream(
            reportPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var report = await JsonSerializer.DeserializeAsync<OutboxCapacityReport>(
            stream,
            CreateJsonOptions(),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Outbox 容量 checkpoint 不能反序列化为有效报告。");
        ValidateCompatibility(options, scenarios, report);
        return new OutboxCapacityCheckpoint(
            options,
            scenarios,
            report.Providers);
    }

    private static void ValidateCompatibility(
        OutboxCapacityOptions options,
        IReadOnlyList<OutboxCapacityScenario> scenarios,
        OutboxCapacityReport report)
    {
        if (!string.Equals(
                report.SourceVersion,
                OutboxCapacityReportWriter.GetSourceVersion(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Outbox 容量 checkpoint 的源版本与当前构建不一致，禁止混合采样。");
        }

        if (!OptionsMatch(options, report.Options)
            || !scenarios.SequenceEqual(report.Scenarios))
        {
            throw new InvalidOperationException(
                "Outbox 容量 checkpoint 的矩阵参数与当前运行不一致，禁止混合采样。");
        }

        if (report.Providers
                .Select(provider => provider.Provider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            != report.Providers.Count
            || report.Providers.Any(provider =>
                !options.Providers.Any(optionProvider =>
                    ProviderEquals(optionProvider, provider.Provider))))
        {
            throw new InvalidOperationException(
                "Outbox 容量 checkpoint 包含重复或未知 Provider。");
        }

        var runKeys = report.Providers
            .SelectMany(provider => provider.Runs.Select(run => (
                Provider: provider.Provider.ToLowerInvariant(),
                run.Scenario,
                run.Repetition)))
            .ToArray();
        var recoveryKeys = report.Providers
            .SelectMany(provider => provider.Recoveries.Select(recovery => (
                Provider: provider.Provider.ToLowerInvariant(),
                recovery.Repetition)))
            .ToArray();
        if (runKeys.Distinct().Count() != runKeys.Length
            || recoveryKeys.Distinct().Count() != recoveryKeys.Length
            || runKeys.Any(key =>
                !scenarios.Contains(key.Scenario)
                || key.Repetition < 1
                || key.Repetition > options.Repetitions)
            || recoveryKeys.Any(key =>
                !options.RecoveryEnabled
                || key.Repetition < 1
                || key.Repetition > options.Repetitions))
        {
            throw new InvalidOperationException(
                "Outbox 容量 checkpoint 包含重复或越界的完成键。");
        }
    }

    private static bool OptionsMatch(
        OutboxCapacityOptions current,
        OutboxCapacityOptions checkpoint) =>
        // 输出目录、续跑开关和单次新增预算不改变样本语义，其余矩阵参数必须完全一致。
        current.Providers.SequenceEqual(
            checkpoint.Providers,
            StringComparer.OrdinalIgnoreCase)
        && current.ConcurrencyLevels.SequenceEqual(
            checkpoint.ConcurrencyLevels)
        && current.HandlerDelayMilliseconds.SequenceEqual(
            checkpoint.HandlerDelayMilliseconds)
        && current.ReplicaCounts.SequenceEqual(checkpoint.ReplicaCounts)
        && current.BatchSizes.SequenceEqual(checkpoint.BatchSizes)
        && current.PayloadSizes.SequenceEqual(checkpoint.PayloadSizes)
        && current.Repetitions == checkpoint.Repetitions
        && current.Warmup == checkpoint.Warmup
        && current.Duration == checkpoint.Duration
        && current.SeedMessages == checkpoint.SeedMessages
        && current.Lease == checkpoint.Lease
        && current.LeaseRenewal == checkpoint.LeaseRenewal
        && current.RecoveryEnabled == checkpoint.RecoveryEnabled
        && current.RecoveryGrace == checkpoint.RecoveryGrace;

    private static bool ProviderEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static JsonSerializerOptions CreateJsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };
}
