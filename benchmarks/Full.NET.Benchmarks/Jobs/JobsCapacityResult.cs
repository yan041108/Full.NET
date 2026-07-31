using Full.NET.Benchmarks.MixedLoad;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsCapacityRunResult(
    string Provider,
    JobsCapacityScenario Scenario,
    int Repetition,
    double ActualDurationSeconds,
    long TerminalExecutions,
    long SucceededExecutions,
    long FailedExecutions,
    long PendingExecutions,
    long RunningExecutions,
    long TerminalExecutionsWithLease,
    long AttemptCountGreaterThanOne,
    long HandlerInvocations,
    long HandlerExpectedFailures,
    double TerminalsPerSecond,
    JobsCapacityStatistics? HandlerLatency,
    JobsCapacityStatistics? QueueLatency,
    long LeaseRenewalExecutions,
    MixedLoadDapperSnapshot Dapper,
    MixedLoadConnectionPoolSnapshot ConnectionPool,
    MixedLoadContainerSnapshot DatabaseContainer,
    IReadOnlyList<string> ProcessorErrors)
{
    public bool CorrectnessGatePassed =>
        double.IsFinite(ActualDurationSeconds)
        && ActualDurationSeconds > 0d
        && double.IsFinite(TerminalsPerSecond)
        && TerminalsPerSecond > 0d
        && DrainDuration >= TimeSpan.Zero
        && TerminalExecutions > 0
        && TerminalExecutions == HandlerInvocations
        && HasValidLatencyEvidence(
            HandlerLatency,
            HandlerInvocations)
        && HasValidLatencyEvidence(
            QueueLatency,
            TerminalExecutions)
        && HasValidProcessEvidence(Process)
        && HasValidDatabaseEvidence(
            DatabaseBefore,
            DatabaseAfter)
        && SucceededExecutions + FailedExecutions
            == TerminalExecutions
        && FailedExecutions == HandlerExpectedFailures
        && AttemptCountGreaterThanOne == 0
        && RunningExecutions == 0
        && TerminalExecutionsWithLease == 0
        && PendingExecutions > 0
        && Dapper.Failures == 0
        && Dapper.Cancellations == 0
        && ProcessorErrors.Count == 0
        && HasValidConnectionPoolEvidence(ConnectionPool)
        && HasValidContainerEvidence(DatabaseContainer);

    private static bool HasValidLatencyEvidence(
        JobsCapacityStatistics? statistics,
        long expectedSampleCount)
    {
        // Checkpoint 反序列化会绕过 Calculate，门禁必须重新验证持久化统计，防止损坏证据进入容量决策。
        return statistics is not null
            && expectedSampleCount > 0
            && statistics.SampleCount == expectedSampleCount
            && double.IsFinite(statistics.MinimumMilliseconds)
            && statistics.MinimumMilliseconds >= 0d
            && double.IsFinite(statistics.P50Milliseconds)
            && statistics.P50Milliseconds
                >= statistics.MinimumMilliseconds
            && double.IsFinite(statistics.P95Milliseconds)
            && statistics.P95Milliseconds
                >= statistics.P50Milliseconds
            && double.IsFinite(statistics.P99Milliseconds)
            && statistics.P99Milliseconds
                >= statistics.P95Milliseconds
            && double.IsFinite(statistics.MaximumMilliseconds)
            && statistics.MaximumMilliseconds
                >= statistics.P99Milliseconds;
    }

    private static bool HasValidProcessEvidence(
        MixedLoadProcessDelta? process) =>
        process is not null
        && double.IsFinite(process.CpuPercent)
        && process.CpuPercent >= 0d
        && process.AllocatedBytes >= 0
        && process.FinalHeapSizeBytes >= 0
        && process.Gen0Collections >= 0
        && process.Gen1Collections >= 0
        && process.Gen2Collections >= 0;

    private static bool HasValidConnectionPoolEvidence(
        MixedLoadConnectionPoolSnapshot? connectionPool) =>
        connectionPool is not null
        && connectionPool.EvidenceComplete
        && connectionPool.EvidenceError is null
        && connectionPool.CapacityHeadroomPassed
        && connectionPool.ConfiguredMaximumConnections > 0
        && connectionPool.MaximumSafeActiveConnections > 0
        && connectionPool.MaximumSafeActiveConnections
            <= connectionPool.ConfiguredMaximumConnections
        && IsPresentFiniteNonNegative(
            connectionPool.PeakActiveConnections)
        && IsPresentFiniteNonNegative(
            connectionPool.PeakIdleConnections)
        && IsPresentFiniteNonNegative(
            connectionPool.PeakPooledConnections)
        && IsFiniteNonNegative(connectionPool.PeakPendingRequests)
        && IsFiniteNonNegative(connectionPool.PeakStasisConnections)
        && IsNonNegative(connectionPool.ConnectionTimeouts)
        && IsNonNegative(connectionPool.ReclaimedConnections)
        && connectionPool.PublishedInstruments is { Count: > 0 }
        && !string.IsNullOrWhiteSpace(connectionPool.ObservationMode)
        && (connectionPool.WaitDuration is null
            || HasValidLatencyEvidence(connectionPool.WaitDuration));

    private static bool HasValidContainerEvidence(
        MixedLoadContainerSnapshot? container) =>
        container is not null
        && container.EvidenceComplete
        && container.EvidenceError is null
        && container.SampleCount > 0
        && double.IsFinite(container.AverageCpuPercentOfHost)
        && container.AverageCpuPercentOfHost >= 0d
        && double.IsFinite(container.PeakCpuPercentOfHost)
        && container.PeakCpuPercentOfHost
            >= container.AverageCpuPercentOfHost;

    private static bool HasValidLatencyEvidence(
        MixedLoadLatencyStatistics statistics) =>
        statistics.SampleCount > 0
        && double.IsFinite(statistics.MinimumMilliseconds)
        && statistics.MinimumMilliseconds >= 0d
        && double.IsFinite(statistics.P50Milliseconds)
        && statistics.P50Milliseconds >= statistics.MinimumMilliseconds
        && double.IsFinite(statistics.P95Milliseconds)
        && statistics.P95Milliseconds >= statistics.P50Milliseconds
        && double.IsFinite(statistics.P99Milliseconds)
        && statistics.P99Milliseconds >= statistics.P95Milliseconds
        && double.IsFinite(statistics.MaximumMilliseconds)
        && statistics.MaximumMilliseconds >= statistics.P99Milliseconds;

    private static bool IsPresentFiniteNonNegative(double? value) =>
        value is not null
        && double.IsFinite(value.Value)
        && value.Value >= 0d;

    private static bool IsFiniteNonNegative(double? value) =>
        value is null
        || double.IsFinite(value.Value)
        && value.Value >= 0d;

    private static bool HasValidDatabaseEvidence(
        MixedLoadDatabaseSnapshot? before,
        MixedLoadDatabaseSnapshot? after) =>
        before is not null
        && after is not null
        && before.MetricsError is null
        && after.MetricsError is null
        && before.CapturedAtUtc != default
        && after.CapturedAtUtc >= before.CapturedAtUtc
        && HasValidDatabaseSnapshot(before)
        && HasValidDatabaseSnapshot(after);

    private static bool HasValidDatabaseSnapshot(
        MixedLoadDatabaseSnapshot snapshot) =>
        snapshot.AccessLogCount >= 0
        && snapshot.PendingOutboxCount >= 0
        && snapshot.OperationLogCount >= 0
        && snapshot.ExceptionLogCount >= 0
        && IsNonNegative(snapshot.DatabaseSessions)
        && IsNonNegative(snapshot.ActiveLocks)
        && IsNonNegative(snapshot.LockWaitCount)
        && (snapshot.LockWaitMilliseconds is null
            || double.IsFinite(snapshot.LockWaitMilliseconds.Value)
            && snapshot.LockWaitMilliseconds.Value >= 0d)
        && IsNonNegative(snapshot.LogBytesWritten)
        && IsNonNegative(snapshot.UndoHistoryLength);

    private static bool IsNonNegative(long? value) =>
        value is null or >= 0;

    public TimeSpan DrainDuration { get; init; }

    public MixedLoadProcessDelta? Process { get; init; }

    public MixedLoadDatabaseSnapshot? DatabaseBefore { get; init; }

    public MixedLoadDatabaseSnapshot? DatabaseAfter { get; init; }
}
