using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Benchmarks.MixedLoad;

namespace Full.NET.Benchmarks.Outbox;

public sealed record OutboxCapacityRunResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    OutboxCapacityScenario Scenario,
    int Repetition,
    double ActualDurationSeconds,
    long CompletedMessages,
    long UniqueMessages,
    long DuplicateDeliveries,
    double MessagesPerSecond,
    MixedLoadLatencyStatistics? HandlerLatency,
    long LeaseRenewalExecutions,
    MixedLoadDapperSnapshot Dapper,
    MixedLoadConnectionPoolSnapshot ConnectionPool,
    MixedLoadContainerSnapshot DatabaseContainer,
    MixedLoadProcessDelta Process,
    MixedLoadDatabaseSnapshot DatabaseBefore,
    MixedLoadDatabaseSnapshot DatabaseAfter,
    IReadOnlyList<string> ProcessorErrors)
{
    public bool BacklogSustained =>
        DatabaseBefore.PendingOutboxCount > 0
        && DatabaseAfter.PendingOutboxCount > 0;

    public bool CorrectnessGatePassed =>
        CompletedMessages > 0
        && DuplicateDeliveries == 0
        && Dapper.Failures == 0
        && ProcessorErrors.Count == 0
        && BacklogSustained;
}

public sealed record OutboxCapacityProviderResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    IReadOnlyList<OutboxCapacityRunResult> Runs,
    IReadOnlyList<OutboxCapacityRecoveryResult> Recoveries);

public sealed record OutboxCapacityRecoveryResult(
    string Provider,
    int Repetition,
    Guid AbandonedMessageId,
    Guid? RecoveredMessageId,
    double RecoveryDurationMilliseconds,
    double LeaseMilliseconds,
    double RecoveryGraceMilliseconds,
    int Attempts,
    long DuplicateDeliveries,
    long DapperFailures,
    long PendingBefore,
    long PendingAfter,
    long DapperCancellations = 0,
    long AcquireExecutions = 0)
{
    public bool StableMessageIdentity =>
        RecoveredMessageId == AbandonedMessageId;

    public bool LeaseBoundaryRespected =>
        RecoveryDurationMilliseconds >= LeaseMilliseconds - 500d;

    public bool RecoveredWithinBudget =>
        RecoveryDurationMilliseconds
        <= LeaseMilliseconds + RecoveryGraceMilliseconds;

    public bool CorrectnessGatePassed =>
        StableMessageIdentity
        && LeaseBoundaryRespected
        && RecoveredWithinBudget
        && Attempts == 2
        && DuplicateDeliveries == 1
        && DapperFailures == 0
        && DapperCancellations == 0
        && AcquireExecutions > 0
        && PendingBefore == 1
        && PendingAfter == 0;

    public static OutboxCapacityRecoveryResult Create(
        string provider,
        int repetition,
        Guid abandonedMessageId,
        Guid? recoveredMessageId,
        TimeSpan recoveryDuration,
        TimeSpan lease,
        TimeSpan recoveryGrace,
        int attempts,
        long duplicateDeliveries,
        long dapperFailures,
        long pendingBefore,
        long pendingAfter,
        long dapperCancellations = 0,
        long acquireExecutions = 0) => new(
        provider,
        repetition,
        abandonedMessageId,
        recoveredMessageId,
        recoveryDuration.TotalMilliseconds,
        lease.TotalMilliseconds,
        recoveryGrace.TotalMilliseconds,
        attempts,
        duplicateDeliveries,
        dapperFailures,
        pendingBefore,
        pendingAfter,
        dapperCancellations,
        acquireExecutions);

    public static long CountAcquireExecutions(
        IReadOnlyDictionary<string, long> statementExecutions)
    {
        ArgumentNullException.ThrowIfNull(statementExecutions);
        return statementExecutions
            .Where(pair =>
                string.Equals(
                    pair.Key,
                    "outbox.acquire.sql_server",
                    StringComparison.Ordinal)
                || string.Equals(
                    pair.Key,
                    "outbox.select_claimable_ids.my_sql",
                    StringComparison.Ordinal))
            .Sum(pair => pair.Value);
    }
}

public sealed record OutboxCapacityReport(
    Guid ReportId,
    DateTimeOffset GeneratedAtUtc,
    string SourceVersion,
    string Framework,
    string OperatingSystem,
    int ProcessorCount,
    OutboxCapacityOptions Options,
    IReadOnlyList<OutboxCapacityScenario> Scenarios,
    IReadOnlyList<string> RequiredMetrics,
    IReadOnlyList<OutboxCapacityProviderResult> Providers)
{
    /// <summary>
    /// 获取完整矩阵预期执行的普通容量采样数量。
    /// </summary>
    public int ExpectedRunCount =>
        Options.Providers.Count * Scenarios.Count * Options.Repetitions;

    /// <summary>
    /// 获取当前报告已经持久化的普通容量采样数量。
    /// </summary>
    public int CompletedRunCount =>
        Providers.Sum(provider => provider.Runs.Count);

    /// <summary>
    /// 获取完整矩阵预期执行的遗弃租约恢复采样数量。
    /// </summary>
    public int ExpectedRecoveryCount =>
        Options.RecoveryEnabled
            ? Options.Providers.Count * Options.Repetitions
            : 0;

    /// <summary>
    /// 获取当前报告已经持久化的遗弃租约恢复采样数量。
    /// </summary>
    public int CompletedRecoveryCount =>
        Providers.Sum(provider => provider.Recoveries.Count);

    /// <summary>
    /// 获取当前报告是否已覆盖全部普通场景和恢复轮次。
    /// </summary>
    public bool IsComplete =>
        CompletedRunCount == ExpectedRunCount
        && CompletedRecoveryCount == ExpectedRecoveryCount;
}

public static class OutboxCapacityReportWriter
{
    public static OutboxCapacityRunResult CreateRunResult(
        string provider,
        string containerImage,
        string databaseVersion,
        OutboxCapacityScenario scenario,
        int repetition,
        TimeSpan actualDuration,
        OutboxCapacityHandlerSnapshot handler,
        MixedLoadDapperSnapshot dapper,
        MixedLoadConnectionPoolSnapshot connectionPool,
        MixedLoadContainerSnapshot databaseContainer,
        MixedLoadProcessSnapshot processBefore,
        MixedLoadProcessSnapshot processAfter,
        MixedLoadDatabaseSnapshot databaseBefore,
        MixedLoadDatabaseSnapshot databaseAfter,
        IReadOnlyList<string> processorErrors)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            actualDuration.TotalMilliseconds);
        var leaseRenewals = dapper.StatementExecutions.GetValueOrDefault(
            "outbox.renew_lease");
        return new OutboxCapacityRunResult(
            provider,
            containerImage,
            databaseVersion,
            scenario,
            repetition,
            actualDuration.TotalSeconds,
            handler.CompletedMessages,
            handler.UniqueMessages,
            handler.DuplicateDeliveries,
            handler.CompletedMessages / actualDuration.TotalSeconds,
            handler.Latency,
            leaseRenewals,
            dapper,
            connectionPool,
            databaseContainer,
            CalculateProcessDelta(
                processBefore,
                processAfter,
                actualDuration),
            databaseBefore,
            databaseAfter,
            processorErrors);
    }

    public static async Task WriteAsync(
        OutboxCapacityOptions options,
        IReadOnlyList<OutboxCapacityScenario> scenarios,
        IReadOnlyList<OutboxCapacityProviderResult> providers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var report = new OutboxCapacityReport(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            GetSourceVersion(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            options,
            scenarios,
            [
                "messages_per_second",
                "handler_p95_p99",
                "duplicate_deliveries",
                "lease_renewal_executions",
                "connection_pool",
                "database_locks_log",
                "process_gc_allocations",
                "database_container_cpu_memory",
                "backlog_sustained",
                "abandoned_lease_recovery",
            ],
            providers);
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

    internal static string GetSourceVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";

    private static string BuildMarkdown(OutboxCapacityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Outbox 消费容量矩阵");
        builder.AppendLine();
        builder.AppendLine(
            $"生成时间：{report.GeneratedAtUtc:O}；场景数：{report.Scenarios.Count}；"
            + $"重复次数：{report.Options.Repetitions}。");
        builder.AppendLine(
            $"进度：场景 {report.CompletedRunCount}/{report.ExpectedRunCount}；"
            + $"恢复 {report.CompletedRecoveryCount}/{report.ExpectedRecoveryCount}；"
            + $"状态：{(report.IsComplete ? "COMPLETE" : "PARTIAL")}。");
        builder.AppendLine();
        builder.AppendLine(
            "| Provider | 场景 | 重复 | msg/s | P95 ms | P99 ms | 重复投递 | 续租 | "
            + "期末积压 | 锁等待 ms Δ | 日志字节 Δ | 正确性门禁 |");
        builder.AppendLine(
            "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var run in report.Providers.SelectMany(provider => provider.Runs))
        {
            var lockWaitBefore =
                run.DatabaseBefore.LockWaitMilliseconds ?? 0d;
            var lockWaitAfter =
                run.DatabaseAfter.LockWaitMilliseconds ?? lockWaitBefore;
            var logBefore = run.DatabaseBefore.LogBytesWritten ?? 0L;
            var logAfter = run.DatabaseAfter.LogBytesWritten ?? logBefore;
            builder.AppendLine(
                $"| {run.Provider} | {run.Scenario.Name} | {run.Repetition} "
                + $"| {run.MessagesPerSecond:F2} "
                + $"| {run.HandlerLatency?.P95Milliseconds:F2} "
                + $"| {run.HandlerLatency?.P99Milliseconds:F2} "
                + $"| {run.DuplicateDeliveries} | {run.LeaseRenewalExecutions} "
                + $"| {run.DatabaseAfter.PendingOutboxCount} "
                + $"| {Math.Max(0d, lockWaitAfter - lockWaitBefore):F2} "
                + $"| {Math.Max(0L, logAfter - logBefore)} "
                + $"| {(run.CorrectnessGatePassed ? "PASS" : "FAIL")} |");
        }

        var recoveries = report.Providers
            .SelectMany(provider => provider.Recoveries)
            .ToArray();
        if (recoveries.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## 遗弃租约恢复");
            builder.AppendLine();
            builder.AppendLine(
                "| Provider | 重复 | 恢复 ms | 租约 ms | Attempts | 重复投递 "
                + "| Acquire SQL | 受控取消 | 期末积压 | 正确性门禁 |");
            builder.AppendLine(
                "|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var recovery in recoveries)
            {
                builder.AppendLine(
                    $"| {recovery.Provider} | {recovery.Repetition} "
                    + $"| {recovery.RecoveryDurationMilliseconds:F2} "
                    + $"| {recovery.LeaseMilliseconds:F0} "
                    + $"| {recovery.Attempts} "
                    + $"| {recovery.DuplicateDeliveries} "
                    + $"| {recovery.AcquireExecutions} "
                    + $"| {recovery.DapperCancellations} "
                    + $"| {recovery.PendingAfter} "
                    + $"| {(recovery.CorrectnessGatePassed ? "PASS" : "FAIL")} |");
            }
        }

        return builder.ToString();
    }

    private static MixedLoadProcessDelta CalculateProcessDelta(
        MixedLoadProcessSnapshot before,
        MixedLoadProcessSnapshot after,
        TimeSpan duration)
    {
        var processorDelta = Math.Max(
            0d,
            after.TotalProcessorMilliseconds
            - before.TotalProcessorMilliseconds);
        return new MixedLoadProcessDelta(
            processorDelta
            / Math.Max(1d, duration.TotalMilliseconds)
            / Math.Max(1, Environment.ProcessorCount)
            * 100d,
            Math.Max(0L, after.TotalAllocatedBytes - before.TotalAllocatedBytes),
            after.HeapSizeBytes,
            Math.Max(0, after.Gen0Collections - before.Gen0Collections),
            Math.Max(0, after.Gen1Collections - before.Gen1Collections),
            Math.Max(0, after.Gen2Collections - before.Gen2Collections));
    }

    private static JsonSerializerOptions CreateJsonOptions(
        bool writeIndented) =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            Converters = { new JsonStringEnumConverter() },
        };

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
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

public sealed record OutboxCapacityHandlerSnapshot(
    long CompletedMessages,
    long UniqueMessages,
    long DuplicateDeliveries,
    MixedLoadLatencyStatistics? Latency);
