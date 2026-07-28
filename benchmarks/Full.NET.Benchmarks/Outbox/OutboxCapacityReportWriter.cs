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
    IReadOnlyList<OutboxCapacityRunResult> Runs);

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
    IReadOnlyList<OutboxCapacityProviderResult> Providers);

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
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "unknown",
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
            ],
            providers);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, "report.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            Encoding.UTF8,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, "summary.md"),
            BuildMarkdown(report),
            Encoding.UTF8,
            cancellationToken);
    }

    private static string BuildMarkdown(OutboxCapacityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Outbox 消费容量矩阵");
        builder.AppendLine();
        builder.AppendLine(
            $"生成时间：{report.GeneratedAtUtc:O}；场景数：{report.Scenarios.Count}；"
            + $"重复次数：{report.Options.Repetitions}。");
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
}

public sealed record OutboxCapacityHandlerSnapshot(
    long CompletedMessages,
    long UniqueMessages,
    long DuplicateDeliveries,
    MixedLoadLatencyStatistics? Latency);
