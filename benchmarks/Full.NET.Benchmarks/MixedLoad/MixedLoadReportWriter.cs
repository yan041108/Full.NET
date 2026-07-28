using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.MixedLoad;

public sealed record MixedLoadRequestSample(
    long Sequence,
    int WorkerId,
    string Scenario,
    DateTimeOffset StartedAtUtc,
    double DurationMilliseconds,
    int? StatusCode,
    int ExpectedStatusCode,
    string? Error,
    MixedLoadAuditWriteProfile AuditWriteProfile =
        MixedLoadAuditWriteProfile.All)
{
    public bool IsUnexpected =>
        Error is not null || StatusCode != ExpectedStatusCode;
}

public sealed record MixedLoadLatencyStatistics(
    int SampleCount,
    double MinimumMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds)
{
    public static MixedLoadLatencyStatistics Calculate(
        IReadOnlyCollection<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("至少需要一个延迟样本。", nameof(samples));
        }

        var ordered = samples.Order().ToArray();
        return new MixedLoadLatencyStatistics(
            ordered.Length,
            ordered[0],
            NearestRank(ordered, 0.50d),
            NearestRank(ordered, 0.95d),
            NearestRank(ordered, 0.99d),
            ordered[^1]);
    }

    private static double NearestRank(
        IReadOnlyList<double> ordered,
        double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * ordered.Count);
        return ordered[Math.Max(0, rank - 1)];
    }
}

public sealed record MixedLoadDapperSnapshot(
    IReadOnlyDictionary<string, long> StatementExecutions,
    MixedLoadLatencyStatistics? Duration,
    long Failures,
    long Cancellations = 0)
{
    /// <summary>
    /// 按稳定 StatementName 汇总非取消数据库失败，保留低基数根因定位线索。
    /// </summary>
    public IReadOnlyDictionary<string, long> FailureStatements { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// 按稳定低基数原因汇总非取消数据库失败，避免将异常文本写入性能工件。
    /// </summary>
    public IReadOnlyDictionary<string, long> FailureReasons { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
}

public sealed record MixedLoadProcessSnapshot(
    DateTimeOffset CapturedAtUtc,
    double TotalProcessorMilliseconds,
    long TotalAllocatedBytes,
    long HeapSizeBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

public sealed record MixedLoadProcessDelta(
    double CpuPercent,
    long AllocatedBytes,
    long FinalHeapSizeBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

public sealed record MixedLoadDatabaseSnapshot(
    DateTimeOffset CapturedAtUtc,
    long AccessLogCount,
    long PendingOutboxCount,
    DateTimeOffset? OldestPendingOutboxAtUtc,
    long? DatabaseSessions,
    long? ActiveLocks,
    long? LockWaitCount,
    double? LockWaitMilliseconds,
    string? MetricsError,
    long OperationLogCount = 0,
    long ExceptionLogCount = 0,
    long? LogBytesWritten = null,
    long? UndoHistoryLength = null);

public sealed record MixedLoadDatabaseDelta(
    long AccessLogsWritten,
    long PendingOutboxGrowth,
    double? OldestPendingOutboxAgeSeconds,
    long? SessionsBefore,
    long? SessionsAfter,
    long? ActiveLocksBefore,
    long? ActiveLocksAfter,
    long? LockWaitCountDelta,
    double? LockWaitMillisecondsDelta,
    string? MetricsError,
    long OperationLogsWritten = 0,
    long ExceptionLogsWritten = 0,
    long? LogBytesWrittenDelta = null,
    long? UndoHistoryLengthBefore = null,
    long? UndoHistoryLengthAfter = null);

public sealed record MixedLoadOutboxOperationSample(
    DateTimeOffset StartedAtUtc,
    string Operation,
    double DurationMilliseconds,
    int ItemCount,
    string? Error);

public sealed record MixedLoadOutboxActivitySnapshot(
    MixedLoadOutboxRetentionProfile Profile,
    IReadOnlyList<MixedLoadOutboxOperationSample> Samples);

public sealed record MixedLoadEvidenceEvaluation(
    bool ScenarioCoverageComplete,
    bool DapperComplete,
    bool DatabaseMetricsComplete,
    bool ConnectionPoolComplete,
    bool ContainerResourcesComplete,
    bool AuditWriteAttributionComplete,
    IReadOnlyList<string> FailureReasons)
{
    public bool Passed =>
        ScenarioCoverageComplete
        && DapperComplete
        && DatabaseMetricsComplete
        && ConnectionPoolComplete
        && ContainerResourcesComplete
        && AuditWriteAttributionComplete;
}

public sealed record MixedLoadScenarioResult(
    string Name,
    int Requests,
    int UnexpectedErrors,
    MixedLoadLatencyStatistics Latency);

public sealed record MixedLoadOutboxActivityResult(
    MixedLoadOutboxRetentionProfile Profile,
    MixedLoadLatencyStatistics RequestLatency,
    MixedLoadLatencyStatistics WorkerLatency,
    long ProcessedMessages,
    long DeletedMessages,
    double DeletedMessagesPerSecond,
    long WorkerErrors,
    long CleanupErrors,
    MixedLoadLatencyStatistics? CleanupLatency,
    bool EvidenceComplete,
    string? EvidenceError)
{
    public string RawSampleFile { get; init; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<MixedLoadOutboxOperationSample> Samples { get; init; } = [];
}

public sealed record MixedLoadOutboxRetentionComparison(
    double MaximumP99RegressionRatio,
    double RequestP99RegressionRatio,
    double WorkerP99RegressionRatio,
    bool RequestP99Passed,
    bool WorkerP99Passed,
    bool ReliabilityPassed,
    bool CleanupProgressPassed)
{
    public const double RequestP99GuardBandMilliseconds = 100d;

    public const double WorkerP99GuardBandMilliseconds = 250d;

    public bool Passed =>
        RequestP99Passed
        && WorkerP99Passed
        && ReliabilityPassed
        && CleanupProgressPassed;

    public static MixedLoadOutboxRetentionComparison Evaluate(
        MixedLoadOutboxActivityResult off,
        MixedLoadOutboxActivityResult on,
        double maximumP99RegressionRatio)
    {
        ArgumentNullException.ThrowIfNull(off);
        ArgumentNullException.ThrowIfNull(on);
        if (off.Profile != MixedLoadOutboxRetentionProfile.Off
            || on.Profile != MixedLoadOutboxRetentionProfile.On)
        {
            throw new ArgumentException(
                "Outbox retention 对比必须按 off/on 顺序提供。");
        }

        if (maximumP99RegressionRatio is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumP99RegressionRatio),
                maximumP99RegressionRatio,
                "P99 退化比例必须位于 0..1。");
        }

        var requestRegression = CalculateRegression(
            off.RequestLatency.P99Milliseconds,
            on.RequestLatency.P99Milliseconds);
        var workerRegression = CalculateRegression(
            off.WorkerLatency.P99Milliseconds,
            on.WorkerLatency.P99Milliseconds);
        return new MixedLoadOutboxRetentionComparison(
            maximumP99RegressionRatio,
            requestRegression,
            workerRegression,
            requestRegression <= maximumP99RegressionRatio
                || on.RequestLatency.P99Milliseconds
                    <= RequestP99GuardBandMilliseconds,
            workerRegression <= maximumP99RegressionRatio
                || on.WorkerLatency.P99Milliseconds
                    <= WorkerP99GuardBandMilliseconds,
            off.WorkerErrors == 0
                && off.CleanupErrors == 0
                && on.WorkerErrors == 0
                && on.CleanupErrors == 0
                && off.EvidenceComplete
                && on.EvidenceComplete,
            off.DeletedMessages == 0 && on.DeletedMessages > 0);
    }

    private static double CalculateRegression(double baseline, double candidate)
    {
        if (baseline <= 0d)
        {
            return candidate <= 0d ? 0d : double.PositiveInfinity;
        }

        return (candidate - baseline) / baseline;
    }
}

public sealed record MixedLoadProviderBudget(
    string Provider,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumUnexpectedErrorRate,
    double MaximumHostProcessCpuPercent,
    double MaximumDatabaseContainerCpuPercent)
{
    public static MixedLoadProviderBudget Create(
        string provider,
        double maximumUnexpectedErrorRate) =>
        provider switch
        {
            "sqlserver" => new MixedLoadProviderBudget(
                provider,
                P95Milliseconds: 750d,
                P99Milliseconds: 2500d,
                maximumUnexpectedErrorRate,
                MaximumHostProcessCpuPercent: 85d,
                MaximumDatabaseContainerCpuPercent: 85d),
            "mysql" => new MixedLoadProviderBudget(
                provider,
                P95Milliseconds: 1000d,
                P99Milliseconds: 3000d,
                maximumUnexpectedErrorRate,
                MaximumHostProcessCpuPercent: 85d,
                MaximumDatabaseContainerCpuPercent: 85d),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的预算 Provider。"),
        };
}

public sealed record MixedLoadBudgetEvaluation(
    bool P95Passed,
    bool P99Passed,
    bool ErrorRatePassed,
    bool HostProcessCpuPassed,
    bool DatabaseContainerCpuPassed,
    bool EvidencePassed)
{
    public bool Passed =>
        P95Passed
        && P99Passed
        && ErrorRatePassed
        && HostProcessCpuPassed
        && DatabaseContainerCpuPassed
        && EvidencePassed;
}

public sealed record MixedLoadRunResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    int Concurrency,
    TimeSpan ConfiguredDuration,
    TimeSpan ActualDuration,
    int RequestCount,
    double RequestsPerSecond,
    int UnexpectedErrors,
    double UnexpectedErrorRate,
    MixedLoadLatencyStatistics Latency,
    IReadOnlyDictionary<string, int> StatusCodes,
    IReadOnlyList<MixedLoadScenarioResult> Scenarios,
    MixedLoadDapperSnapshot Dapper,
    MixedLoadAuditWriteAttributionResult AuditWrites,
    MixedLoadConnectionPoolSnapshot ConnectionPool,
    MixedLoadContainerSnapshot DatabaseContainer,
    MixedLoadProcessDelta Process,
    MixedLoadDatabaseDelta Database,
    MixedLoadEvidenceEvaluation Evidence,
    MixedLoadProviderBudget Budget,
    MixedLoadBudgetEvaluation BudgetEvaluation,
    string RawSampleFile)
{
    public MixedLoadOutboxActivityResult? OutboxActivity { get; init; }

    [JsonIgnore]
    public IReadOnlyList<MixedLoadRequestSample> Samples { get; init; } = [];
}

public sealed record MixedLoadProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    IReadOnlyList<MixedLoadRunResult> Runs);

public sealed record MixedLoadBenchmarkReport(
    Guid ReportId,
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    MixedLoadOptions Options,
    IReadOnlyList<MixedLoadScenario> Workload,
    IReadOnlyList<string> RequiredMetrics,
    IReadOnlyList<MixedLoadProviderResult> Providers);

public static class MixedLoadReportWriter
{
    public static MixedLoadRunResult CreateRunResult(
        string provider,
        string containerImage,
        string databaseVersion,
        int concurrency,
        MixedLoadOptions options,
        TimeSpan actualDuration,
        IReadOnlyList<MixedLoadRequestSample> samples,
        MixedLoadDapperSnapshot dapper,
        MixedLoadAuditWriteSnapshot auditWrites,
        MixedLoadConnectionPoolSnapshot connectionPool,
        MixedLoadContainerSnapshot databaseContainer,
        MixedLoadProcessSnapshot processBefore,
        MixedLoadProcessSnapshot processAfter,
        MixedLoadDatabaseSnapshot databaseBefore,
        MixedLoadDatabaseSnapshot databaseAfter,
        MixedLoadOutboxActivitySnapshot? outboxActivity = null)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider {provider} 并发 {concurrency} 未产生请求样本。");
        }

        var latency = MixedLoadLatencyStatistics.Calculate(
            samples.Select(sample => sample.DurationMilliseconds).ToArray());
        var unexpectedErrors = samples.Count(sample => sample.IsUnexpected);
        var unexpectedErrorRate = (double)unexpectedErrors / samples.Count;
        var requestsPerSecond = samples.Count / actualDuration.TotalSeconds;
        var statusCodes = samples
            .GroupBy(sample => sample.StatusCode?.ToString(CultureInfo.InvariantCulture)
                ?? "exception")
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var scenarioResults = samples
            .GroupBy(sample => sample.Scenario, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new MixedLoadScenarioResult(
                group.Key,
                group.Count(),
                group.Count(sample => sample.IsUnexpected),
                MixedLoadLatencyStatistics.Calculate(
                    group.Select(sample => sample.DurationMilliseconds).ToArray())))
            .ToArray();
        var elapsedWallMilliseconds = Math.Max(
            1d,
            (processAfter.CapturedAtUtc - processBefore.CapturedAtUtc)
                .TotalMilliseconds);
        var cpuPercent = Math.Max(
            0d,
            processAfter.TotalProcessorMilliseconds
            - processBefore.TotalProcessorMilliseconds)
            / elapsedWallMilliseconds
            / Environment.ProcessorCount
            * 100d;
        var process = new MixedLoadProcessDelta(
            cpuPercent,
            Math.Max(
                0,
                processAfter.TotalAllocatedBytes - processBefore.TotalAllocatedBytes),
            processAfter.HeapSizeBytes,
            processAfter.Gen0Collections - processBefore.Gen0Collections,
            processAfter.Gen1Collections - processBefore.Gen1Collections,
            processAfter.Gen2Collections - processBefore.Gen2Collections);
        var database = new MixedLoadDatabaseDelta(
            databaseAfter.AccessLogCount - databaseBefore.AccessLogCount,
            databaseAfter.PendingOutboxCount - databaseBefore.PendingOutboxCount,
            databaseAfter.OldestPendingOutboxAtUtc is { } oldest
                ? Math.Max(0d, (databaseAfter.CapturedAtUtc - oldest).TotalSeconds)
                : null,
            databaseBefore.DatabaseSessions,
            databaseAfter.DatabaseSessions,
            databaseBefore.ActiveLocks,
            databaseAfter.ActiveLocks,
            Difference(databaseBefore.LockWaitCount, databaseAfter.LockWaitCount),
            Difference(
                databaseBefore.LockWaitMilliseconds,
                databaseAfter.LockWaitMilliseconds),
            databaseBefore.MetricsError ?? databaseAfter.MetricsError,
            databaseAfter.OperationLogCount - databaseBefore.OperationLogCount,
            databaseAfter.ExceptionLogCount - databaseBefore.ExceptionLogCount,
            Difference(
                databaseBefore.LogBytesWritten,
                databaseAfter.LogBytesWritten),
            databaseBefore.UndoHistoryLength,
            databaseAfter.UndoHistoryLength);
        var budget = MixedLoadProviderBudget.Create(
            provider,
            options.MaximumUnexpectedErrorRate);
        var workload = MixedLoadScenarioCatalog.Get(options.Workload);
        var expectedScenarios = workload
            .Select(scenario => scenario.Name)
            .ToHashSet(StringComparer.Ordinal);
        var actualScenarios = scenarioResults
            .Select(scenario => scenario.Name)
            .ToHashSet(StringComparer.Ordinal);
        var scenarioCoverageComplete = expectedScenarios.SetEquals(actualScenarios);
        var dapperComplete = dapper.StatementExecutions.Values.Sum() > 0
            && dapper.Duration is not null
            && dapper.Failures == 0;
        var databaseMetricsComplete = database.MetricsError is null;
        var auditWriteAttribution = MixedLoadAuditWriteAttribution.Create(
            samples,
            workload,
            auditWrites,
            options.AuditWriteProfiles);
        var auditDatabaseCountsComplete =
            database.AccessLogsWritten
                == ObservedAuditWrites(
                    auditWriteAttribution,
                    "auditing.insert_access_log")
            && database.OperationLogsWritten
                == ObservedAuditWrites(
                    auditWriteAttribution,
                    "auditing.insert_operation_log")
            && database.ExceptionLogsWritten
                == ObservedAuditWrites(
                    auditWriteAttribution,
                    "auditing.insert_exception_log");
        var failureReasons = new List<string>();
        if (!scenarioCoverageComplete)
        {
            failureReasons.Add("场景覆盖不完整。");
        }

        if (!dapperComplete)
        {
            failureReasons.Add("Dapper 执行次数或耗时指标缺失。");
        }

        if (!databaseMetricsComplete)
        {
            failureReasons.Add($"数据库资源指标不完整：{database.MetricsError}");
        }

        if (!connectionPool.EvidenceComplete)
        {
            failureReasons.Add(
                $"连接池指标不完整：{connectionPool.EvidenceError ?? "未知原因"}");
        }

        if (!databaseContainer.EvidenceComplete)
        {
            failureReasons.Add(
                $"数据库容器资源指标不完整："
                + $"{databaseContainer.EvidenceError ?? "未知原因"}");
        }

        if (!auditWriteAttribution.EvidenceComplete
            || !auditDatabaseCountsComplete)
        {
            failureReasons.Add(
                "Audit profile、写入失败、预期/观测次数或数据库行数证据不完整。");
        }

        var evidence = new MixedLoadEvidenceEvaluation(
            scenarioCoverageComplete,
            dapperComplete,
            databaseMetricsComplete,
            connectionPool.EvidenceComplete,
            databaseContainer.EvidenceComplete,
            auditWriteAttribution.EvidenceComplete
                && auditDatabaseCountsComplete,
            failureReasons);
        var evaluation = new MixedLoadBudgetEvaluation(
            latency.P95Milliseconds <= budget.P95Milliseconds,
            latency.P99Milliseconds <= budget.P99Milliseconds,
            unexpectedErrorRate <= budget.MaximumUnexpectedErrorRate,
            cpuPercent <= budget.MaximumHostProcessCpuPercent,
            databaseContainer.AverageCpuPercentOfHost
                <= budget.MaximumDatabaseContainerCpuPercent,
            evidence.Passed);
        var profileSuffix = outboxActivity is null
            ? string.Empty
            : $"-retention-{outboxActivity.Profile.ToString().ToLowerInvariant()}";
        var rawFile = Path.Combine(
                "raw",
                $"{provider}-c{concurrency}{profileSuffix}-samples.ndjson")
            .Replace('\\', '/');
        var outboxActivityResult = outboxActivity is null
            ? null
            : CreateOutboxActivityResult(
                latency,
                actualDuration,
                provider,
                concurrency,
                outboxActivity);

        return new MixedLoadRunResult(
            provider,
            containerImage,
            databaseVersion,
            concurrency,
            options.Duration,
            actualDuration,
            samples.Count,
            requestsPerSecond,
            unexpectedErrors,
            unexpectedErrorRate,
            latency,
            statusCodes,
            scenarioResults,
            dapper,
            auditWriteAttribution,
            connectionPool,
            databaseContainer,
            process,
            database,
            evidence,
            budget,
            evaluation,
            rawFile)
        {
            Samples = samples,
            OutboxActivity = outboxActivityResult,
        };
    }

    private static MixedLoadOutboxActivityResult CreateOutboxActivityResult(
        MixedLoadLatencyStatistics requestLatency,
        TimeSpan actualDuration,
        string provider,
        int concurrency,
        MixedLoadOutboxActivitySnapshot snapshot)
    {
        var workerSamples = snapshot.Samples
            .Where(sample => sample.Operation == "worker")
            .ToArray();
        var cleanupSamples = snapshot.Samples
            .Where(sample => sample.Operation == "cleanup")
            .ToArray();
        var workerSuccesses = workerSamples
            .Where(sample => sample.Error is null && sample.ItemCount > 0)
            .ToArray();
        var cleanupSuccesses = cleanupSamples
            .Where(sample => sample.Error is null)
            .ToArray();
        var workerErrors = workerSamples.LongCount(sample =>
            sample.Error is not null);
        var cleanupErrors = cleanupSamples.LongCount(sample =>
            sample.Error is not null);
        var evidenceErrors = new List<string>();
        if (workerSuccesses.Length == 0)
        {
            evidenceErrors.Add("未采集到成功的 Outbox Worker 样本。");
        }

        if (snapshot.Profile == MixedLoadOutboxRetentionProfile.Off
            && cleanupSamples.Length > 0)
        {
            evidenceErrors.Add("off profile 不应执行清理。");
        }

        var deletedMessages = cleanupSuccesses.Sum(sample =>
            (long)sample.ItemCount);
        if (snapshot.Profile == MixedLoadOutboxRetentionProfile.On
            && deletedMessages == 0)
        {
            evidenceErrors.Add("on profile 未删除过期成功消息。");
        }

        if (workerErrors > 0 || cleanupErrors > 0)
        {
            evidenceErrors.Add(
                $"Worker/cleanup 错误数为 {workerErrors}/{cleanupErrors}。");
        }

        var rawSampleFile = Path.Combine(
                "raw",
                $"{provider}-c{concurrency}-retention-"
                + $"{snapshot.Profile.ToString().ToLowerInvariant()}"
                + "-outbox.ndjson")
            .Replace('\\', '/');
        return new MixedLoadOutboxActivityResult(
            snapshot.Profile,
            requestLatency,
            MixedLoadLatencyStatistics.Calculate(
                workerSuccesses.Length == 0
                    ? [0d]
                    : workerSuccesses.Select(sample =>
                        sample.DurationMilliseconds).ToArray()),
            workerSuccesses.Sum(sample => (long)sample.ItemCount),
            deletedMessages,
            deletedMessages / Math.Max(0.001d, actualDuration.TotalSeconds),
            workerErrors,
            cleanupErrors,
            cleanupSuccesses.Length == 0
                ? null
                : MixedLoadLatencyStatistics.Calculate(
                    cleanupSuccesses.Select(sample =>
                        sample.DurationMilliseconds).ToArray()),
            evidenceErrors.Count == 0,
            evidenceErrors.Count == 0
                ? null
                : string.Join("；", evidenceErrors))
        {
            RawSampleFile = rawSampleFile,
            Samples = snapshot.Samples,
        };
    }

    public static MixedLoadBenchmarkReport CreateReport(
        MixedLoadOptions options,
        IReadOnlyList<MixedLoadProviderResult> providers)
    {
        var sourceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        return new MixedLoadBenchmarkReport(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            sourceVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options,
            MixedLoadScenarioCatalog.Get(options.Workload),
            MixedLoadMetricContract.Required,
            providers);
    }

    public static async Task<MixedLoadRunResult> WriteRunCheckpointAsync(
        string outputDirectory,
        MixedLoadRunResult run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Samples.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider {run.Provider} 并发 {run.Concurrency} 没有可写入的原始样本。");
        }

        await WriteRawSamplesAsync(
            outputDirectory,
            run,
            cancellationToken);
        if (run.OutboxActivity is { } outboxActivity)
        {
            await WriteRawOutboxSamplesAsync(
                outputDirectory,
                outboxActivity,
                cancellationToken);
        }

        return run with
        {
            Samples = [],
            OutboxActivity = run.OutboxActivity is null
                ? null
                : run.OutboxActivity with
                {
                    Samples = [],
                },
        };
    }

    public static async Task WriteAsync(
        string outputDirectory,
        MixedLoadBenchmarkReport report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "raw"));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        var rawOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var run in report.Providers.SelectMany(provider => provider.Runs))
        {
            var path = Path.Combine(
                outputDirectory,
                run.RawSampleFile.Replace('/', Path.DirectorySeparatorChar));
            if (run.Samples.Count == 0)
            {
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"原始样本检查点不存在：{run.RawSampleFile}");
                }

                continue;
            }

            await WriteRawSamplesAsync(
                outputDirectory,
                run,
                cancellationToken,
                rawOptions);
            if (run.OutboxActivity is { Samples.Count: > 0 } outboxActivity)
            {
                await WriteRawOutboxSamplesAsync(
                    outputDirectory,
                    outboxActivity,
                    cancellationToken,
                    rawOptions);
            }
        }

        await WriteTextAtomicallyAsync(
            Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            cancellationToken);
        await WriteTextAtomicallyAsync(
            Path.Combine(outputDirectory, "README.md"),
            BuildMarkdown(report),
            cancellationToken);
        await WriteTextAtomicallyAsync(
            Path.Combine(outputDirectory, "manifest.json"),
            JsonSerializer.Serialize(
                new
                {
                    report.ReportId,
                    report.SourceVersion,
                    report.Options,
                    report.Workload,
                    report.RequiredMetrics,
                    Budgets = report.Providers.Select(provider =>
                        MixedLoadProviderBudget.Create(
                            provider.Provider,
                            report.Options.MaximumUnexpectedErrorRate)),
                },
                jsonOptions),
            cancellationToken);
    }

    public static void EnsurePassed(MixedLoadBenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var failedRuns = report.Providers
            .SelectMany(provider => provider.Runs)
            .Where(run =>
                !run.BudgetEvaluation.Passed
                || run.OutboxActivity is { EvidenceComplete: false })
            .Select(run =>
                $"{run.Provider}/c={run.Concurrency}: "
                + $"P95={run.BudgetEvaluation.P95Passed}, "
                + $"P99={run.BudgetEvaluation.P99Passed}, "
                + $"error={run.BudgetEvaluation.ErrorRatePassed}, "
                + $"host CPU={run.BudgetEvaluation.HostProcessCpuPassed}, "
                + $"database CPU="
                + $"{run.BudgetEvaluation.DatabaseContainerCpuPassed}, "
                + $"evidence={run.BudgetEvaluation.EvidencePassed}, "
                + $"outbox={run.OutboxActivity?.EvidenceComplete ?? true}"
                + (run.Evidence.FailureReasons.Count == 0
                    ? string.Empty
                    : $" ({string.Join("；", run.Evidence.FailureReasons)})")
                + (run.OutboxActivity?.EvidenceError is null
                    ? string.Empty
                    : $" ({run.OutboxActivity.EvidenceError})"))
            .ToArray();
        var failedComparisons = report.Providers
            .SelectMany(provider => provider.Runs
                .Where(run => run.OutboxActivity is not null)
                .GroupBy(run => run.Concurrency)
                .Select(group =>
                {
                    var off = group.Single(run =>
                        run.OutboxActivity!.Profile
                        == MixedLoadOutboxRetentionProfile.Off);
                    var on = group.Single(run =>
                        run.OutboxActivity!.Profile
                        == MixedLoadOutboxRetentionProfile.On);
                    return new
                    {
                        Provider = provider.Provider,
                        Concurrency = group.Key,
                        Comparison = MixedLoadOutboxRetentionComparison.Evaluate(
                            off.OutboxActivity!,
                            on.OutboxActivity!,
                            maximumP99RegressionRatio: 0.20d),
                    };
                }))
            .Where(item => !item.Comparison.Passed)
            .Select(item =>
                $"{item.Provider}/c={item.Concurrency}: "
                + $"request P99 Δ={item.Comparison.RequestP99RegressionRatio:P3}, "
                + $"worker P99 Δ={item.Comparison.WorkerP99RegressionRatio:P3}, "
                + $"reliability={item.Comparison.ReliabilityPassed}, "
                + $"cleanup progress={item.Comparison.CleanupProgressPassed}")
            .ToArray();
        var failed = failedRuns.Concat(failedComparisons).ToArray();
        if (failed.Length > 0)
        {
            throw new InvalidOperationException(
                "混合负载预算或证据门禁失败：" + Environment.NewLine
                + string.Join(Environment.NewLine, failed));
        }
    }

    private static async Task WriteRawSamplesAsync(
        string outputDirectory,
        MixedLoadRunResult run,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null)
    {
        var path = Path.Combine(
            outputDirectory,
            run.RawSampleFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("原始样本路径缺少目录。"));
        var temporaryPath = CreateTemporaryPath(path);
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                var options = jsonOptions
                    ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
                foreach (var sample in run.Samples)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(
                        JsonSerializer.Serialize(sample, options).AsMemory(),
                        cancellationToken);
                }
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task WriteRawOutboxSamplesAsync(
        string outputDirectory,
        MixedLoadOutboxActivityResult activity,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null)
    {
        if (activity.Samples.Count == 0)
        {
            throw new InvalidOperationException(
                $"Outbox retention {activity.Profile} 没有可写入的原始样本。");
        }

        var path = Path.Combine(
            outputDirectory,
            activity.RawSampleFile.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Outbox 原始样本路径缺少目录。"));
        var temporaryPath = CreateTemporaryPath(path);
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                var options = jsonOptions
                    ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
                foreach (var sample in activity.Samples)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(
                        JsonSerializer.Serialize(sample, options).AsMemory(),
                        cancellationToken);
                }
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = CreateTemporaryPath(path);
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                Encoding.UTF8,
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string CreateTemporaryPath(string path) =>
        $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

    private static string BuildMarkdown(MixedLoadBenchmarkReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Full.NET 生产等价混合负载基线");
        builder.AppendLine();
        builder.AppendLine($"- 报告 ID：`{report.ReportId:D}`");
        builder.AppendLine($"- 生成时间：`{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"- 源版本：`{report.SourceVersion}`");
        builder.AppendLine($"- 运行时：`{report.Framework}`");
        builder.AppendLine($"- 操作系统：`{report.OperatingSystem}`");
        builder.AppendLine($"- 逻辑处理器：`{report.ProcessorCount}`");
        builder.AppendLine(
            $"- 预热：`{report.Options.Warmup.TotalSeconds:0}s`；"
            + $"稳态：`{report.Options.Duration.TotalSeconds:0}s`；"
            + $"种子：`{report.Options.Seed}`");
        builder.AppendLine(
            $"- Workload：`{report.Options.Workload}`；Audit 写入组合："
            + $"`{string.Join(",", report.Options.AuditWriteProfiles)}`");
        if (report.Options.OutboxRetentionMatrixEnabled)
        {
            builder.AppendLine(
                $"- Outbox retention A/B："
                + $"`{string.Join(",", report.Options.OutboxRetentionProfiles)}`；"
                + $"过期成功种子 `{report.Options.OutboxRetentionSeedProcessed}`；"
                + $"批大小 `{report.Options.OutboxRetentionBatchSize}`；"
                + $"单轮上限 `{report.Options.OutboxRetentionMaxBatches}`；"
                + $"批间隔 "
                + $"`{report.Options.OutboxRetentionInterval.TotalMilliseconds:0}ms`");
        }

        builder.AppendLine();
        builder.AppendLine(
            "> 本报告是本机 Testcontainers 与进程内真实 API Host 的回归基线，"
            + "不是生产 SLA，也不能用于跨 Provider 绝对性能排名。");
        builder.AppendLine();
        builder.AppendLine("## Workload");
        builder.AppendLine();
        builder.AppendLine(
            "| 场景 | 权重 | 认证 | Method | 类型 | 预期状态 | Audit | Outbox |");
        builder.AppendLine("| --- | ---: | --- | --- | --- | ---: | --- | --- |");
        foreach (var scenario in report.Workload)
        {
            builder.AppendLine(
                $"| `{scenario.Name}` | {scenario.Weight} | "
                + $"{scenario.Authentication} | {scenario.RequestMethod} | "
                + $"{scenario.Operation} | "
                + $"{(int)scenario.ExpectedStatusCode} | "
                + $"{YesNo(scenario.IsAuditQuery)} | "
                + $"{YesNo(scenario.ProducesOutbox)} |");
        }

        builder.AppendLine();
        foreach (var provider in report.Providers)
        {
            builder.AppendLine($"## {provider.Provider}");
            builder.AppendLine();
            builder.AppendLine($"- 镜像：`{provider.ContainerImage}`");
            builder.AppendLine($"- 数据库版本：`{provider.DatabaseVersion}`");
            builder.AppendLine();
            builder.AppendLine(
                "| 并发 | Retention | 请求 | QPS | P50 ms | P95 ms | P99 ms | "
                + "非预期错误率 | Host CPU | DB CPU | Audit A/O/E Δ | "
                + "Outbox pending Δ | 预算 |");
            builder.AppendLine(
                "| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | "
                + "---: | ---: | ---: | ---: | --- |");
            foreach (var run in provider.Runs)
            {
                builder.AppendLine(
                    $"| {run.Concurrency} | "
                    + $"`{run.OutboxActivity?.Profile.ToString() ?? "-"}` | "
                    + $"{run.RequestCount} | "
                    + $"{Format(run.RequestsPerSecond)} | "
                    + $"{Format(run.Latency.P50Milliseconds)} | "
                    + $"{Format(run.Latency.P95Milliseconds)} | "
                    + $"{Format(run.Latency.P99Milliseconds)} | "
                    + $"{run.UnexpectedErrorRate:P3} | "
                    + $"{Format(run.Process.CpuPercent)}% | "
                    + $"{Format(run.DatabaseContainer.AverageCpuPercentOfHost)}% | "
                    + $"{run.Database.AccessLogsWritten}/"
                    + $"{run.Database.OperationLogsWritten}/"
                    + $"{run.Database.ExceptionLogsWritten} | "
                    + $"{run.Database.PendingOutboxGrowth} | "
                    + $"{(run.BudgetEvaluation.Passed ? "PASS" : "FAIL")} |");
            }

            builder.AppendLine();
            foreach (var comparisonGroup in provider.Runs
                         .Where(run => run.OutboxActivity is not null)
                         .GroupBy(run => run.Concurrency)
                         .Where(group =>
                             group.Select(run => run.OutboxActivity!.Profile)
                                 .Distinct()
                                 .Count() == 2))
            {
                var off = comparisonGroup.Single(run =>
                    run.OutboxActivity!.Profile
                    == MixedLoadOutboxRetentionProfile.Off);
                var on = comparisonGroup.Single(run =>
                    run.OutboxActivity!.Profile
                    == MixedLoadOutboxRetentionProfile.On);
                var comparison = MixedLoadOutboxRetentionComparison.Evaluate(
                    off.OutboxActivity!,
                    on.OutboxActivity!,
                    maximumP99RegressionRatio: 0.20d);
                builder.AppendLine(
                    $"- c={comparisonGroup.Key} retention on/off："
                    + $"请求 P99 Δ `{comparison.RequestP99RegressionRatio:P3}`；"
                    + $"Worker P99 Δ `{comparison.WorkerP99RegressionRatio:P3}`；"
                    + $"可靠性 `{comparison.ReliabilityPassed}`；"
                    + $"清理推进 `{comparison.CleanupProgressPassed}`；"
                    + $"结论 `{(comparison.Passed ? "PASS" : "FAIL")}`");
            }

            if (provider.Runs.Any(run => run.OutboxActivity is not null))
            {
                builder.AppendLine();
            }

            var budget = provider.Runs.FirstOrDefault()?.Budget;
            if (budget is not null)
            {
                builder.AppendLine(
                    $"回归预算：P95 ≤ `{budget.P95Milliseconds:0}ms`，"
                    + $"P99 ≤ `{budget.P99Milliseconds:0}ms`，"
                    + $"非预期错误率 ≤ `{budget.MaximumUnexpectedErrorRate:P3}`，"
                    + $"Host CPU ≤ `{budget.MaximumHostProcessCpuPercent:0}%`，"
                    + $"DB CPU ≤ "
                    + $"`{budget.MaximumDatabaseContainerCpuPercent:0}%`。");
                builder.AppendLine();
            }

            foreach (var run in provider.Runs)
            {
                builder.AppendLine(
                    $"### c={run.Concurrency}"
                    + (run.OutboxActivity is null
                        ? string.Empty
                        : $" retention={run.OutboxActivity.Profile}"));
                builder.AppendLine();
                builder.AppendLine(
                    $"- Dapper 命令：`{run.Dapper.StatementExecutions.Values.Sum()}`；"
                    + $"失败：`{run.Dapper.Failures}`");
                builder.AppendLine();
                builder.AppendLine(
                    "| Audit profile | 请求 | P95 ms | P99 ms | "
                    + "Access 预期/观测/P95 | Operation 预期/观测/P95 | "
                    + "Exception 预期/观测/P95 | 证据 |");
                builder.AppendLine(
                    "| --- | ---: | ---: | ---: | --- | --- | --- | --- |");
                foreach (var profile in run.AuditWrites.Profiles)
                {
                    builder.AppendLine(
                        $"| `{profile.Profile}` | {profile.RequestCount} | "
                        + $"{Format(profile.Latency.P95Milliseconds)} | "
                        + $"{Format(profile.Latency.P99Milliseconds)} | "
                        + $"{FormatAuditObservation(profile, "auditing.insert_access_log")} | "
                        + $"{FormatAuditObservation(profile, "auditing.insert_operation_log")} | "
                        + $"{FormatAuditObservation(profile, "auditing.insert_exception_log")} | "
                        + $"{(profile.EvidenceComplete ? "PASS" : "FAIL")} |");
                }

                builder.AppendLine(
                    $"- 连接池：峰值 active/pooled/pending = "
                    + $"`{run.ConnectionPool.PeakActiveConnections}`/"
                    + $"`{run.ConnectionPool.PeakPooledConnections}`/"
                    + $"`{run.ConnectionPool.PeakPendingRequests}`；"
                    + $"超时 `{run.ConnectionPool.ConnectionTimeouts}`；"
                    + $"active 安全上限 "
                    + $"`{run.ConnectionPool.MaximumSafeActiveConnections}`；"
                    + $"余量通过 `{run.ConnectionPool.CapacityHeadroomPassed}`；"
                    + $"指标完整 `{run.ConnectionPool.EvidenceComplete}`");
                builder.AppendLine(
                    $"- 连接池观测模式：{run.ConnectionPool.ObservationMode}");
                builder.AppendLine(
                    $"- 数据库容器：CPU 平均/峰值 = "
                    + $"`{Format(run.DatabaseContainer.AverageCpuPercentOfHost)}%`/"
                    + $"`{Format(run.DatabaseContainer.PeakCpuPercentOfHost)}%`；"
                    + $"内存峰值 `{run.DatabaseContainer.PeakMemoryBytes}` bytes；"
                    + $"样本 `{run.DatabaseContainer.SampleCount}`");
                if (!run.Evidence.Passed)
                {
                    builder.AppendLine(
                        $"- 证据门禁：FAIL（"
                        + $"{string.Join("；", run.Evidence.FailureReasons)}）");
                }

                builder.AppendLine(
                    $"- GC：Gen0/1/2 = `{run.Process.Gen0Collections}/"
                    + $"{run.Process.Gen1Collections}/{run.Process.Gen2Collections}`；"
                    + $"分配 `{run.Process.AllocatedBytes}` bytes；"
                    + $"最终堆 `{run.Process.FinalHeapSizeBytes}` bytes");
                builder.AppendLine(
                    $"- 数据库会话：`{run.Database.SessionsBefore}` → "
                    + $"`{run.Database.SessionsAfter}`；活动锁："
                    + $"`{run.Database.ActiveLocksBefore}` → "
                    + $"`{run.Database.ActiveLocksAfter}`；锁等待增量："
                    + $"`{run.Database.LockWaitCountDelta}` / "
                    + $"`{run.Database.LockWaitMillisecondsDelta:0.###}ms`");
                builder.AppendLine(
                    $"- 数据库日志写入增量："
                    + $"`{run.Database.LogBytesWrittenDelta}` bytes；"
                    + $"undo history：`{run.Database.UndoHistoryLengthBefore}` → "
                    + $"`{run.Database.UndoHistoryLengthAfter}`");
                if (run.OutboxActivity is { } activity)
                {
                    builder.AppendLine(
                        $"- Outbox Worker：处理 `{activity.ProcessedMessages}`；"
                        + $"P95/P99 "
                        + $"`{Format(activity.WorkerLatency.P95Milliseconds)}`/"
                        + $"`{Format(activity.WorkerLatency.P99Milliseconds)}ms`；"
                        + $"错误 `{activity.WorkerErrors}`");
                    builder.AppendLine(
                        $"- Outbox cleanup：删除 `{activity.DeletedMessages}`；"
                        + $"吞吐 `{Format(activity.DeletedMessagesPerSecond)}/s`；"
                        + $"P95/P99 "
                        + (activity.CleanupLatency is null
                            ? "`-/-`"
                            : $"`{Format(activity.CleanupLatency.P95Milliseconds)}`/"
                              + $"`{Format(activity.CleanupLatency.P99Milliseconds)}ms`")
                        + $"；错误 `{activity.CleanupErrors}`；"
                        + $"证据 `{activity.EvidenceComplete}`");
                    builder.AppendLine(
                        $"- Outbox 原始样本：`{activity.RawSampleFile}`");
                }

                if (run.Database.MetricsError is not null)
                {
                    builder.AppendLine(
                        $"- 数据库资源指标错误：`{run.Database.MetricsError}`");
                }

                builder.AppendLine($"- 原始样本：`{run.RawSampleFile}`");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static long? Difference(long? before, long? after) =>
        before.HasValue && after.HasValue
            ? after.Value - before.Value
            : null;

    private static double? Difference(double? before, double? after) =>
        before.HasValue && after.HasValue
            ? after.Value - before.Value
            : null;

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatAuditObservation(
        MixedLoadAuditWriteProfileResult profile,
        string statementName)
    {
        var expected = profile.ExpectedStatementExecutions[statementName];
        var observed = profile.ObservedStatementExecutions[statementName];
        var observation = profile.Observations.SingleOrDefault(item =>
            string.Equals(
                item.StatementName,
                statementName,
                StringComparison.Ordinal));
        return $"{expected}/{observed}/"
            + (observation?.Duration is { } duration
                ? Format(duration.P95Milliseconds)
                : "-");
    }

    private static long ObservedAuditWrites(
        MixedLoadAuditWriteAttributionResult attribution,
        string statementName) =>
        attribution.Profiles.Sum(profile =>
            profile.ObservedStatementExecutions[statementName]);

    private static string YesNo(bool value) => value ? "是" : "否";
}
