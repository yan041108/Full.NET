namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsBacklogQueryResult(
    long PendingCount,
    DateTimeOffset? OldestClaimableCreatedAtUtc,
    long DueRetryCount,
    DateTimeOffset? OldestDueRetryAtUtc)
{
    public bool Matches(JobsBacklogDatasetExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        return PendingCount == expectation.PendingCount
            && DueRetryCount == expectation.DueRetryCount
            && SameDatabaseTime(
                OldestClaimableCreatedAtUtc,
                expectation.OldestClaimableCreatedAtUtc)
            && SameDatabaseTime(
                OldestDueRetryAtUtc,
                expectation.OldestDueRetryAtUtc);
    }

    private static bool SameDatabaseTime(
        DateTimeOffset? actual,
        DateTimeOffset? expected) =>
        actual is null && expected is null
        || actual is { } actualValue
        && expected is { } expectedValue
        && Math.Abs((actualValue - expectedValue).Ticks)
            <= TimeSpan.TicksPerMicrosecond;
}
