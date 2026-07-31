using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsBacklogSeedRow(
    Guid Id,
    Guid? TenantId,
    Guid JobDefinitionId,
    string Status,
    string TriggerKind,
    DateTimeOffset? NextAttemptAtUtc,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc);

public sealed record JobsBacklogDatasetExpectation(
    long PendingCount,
    long DueRetryCount,
    long ClaimableCount,
    long TenantPendingNoiseCount,
    DateTimeOffset? OldestClaimableCreatedAtUtc,
    DateTimeOffset? OldestDueRetryAtUtc);

public static class JobsBacklogDataset
{
    public const int BucketCount = 20;

    private static readonly Guid HostDefinitionId =
        Guid.Parse("00000000-0000-7000-8000-000000000001");

    private static readonly Guid TenantDefinitionId =
        Guid.Parse("00000000-0000-7000-8000-000000000002");

    private static readonly Guid TenantId =
        Guid.Parse("00000000-0000-7000-8000-000000000003");

    public static JobsBacklogSeedRow CreateRow(
        int index,
        int totalRows,
        DateTimeOffset referenceUtc)
    {
        if (index < 0 || index >= totalRows)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (totalRows < BucketCount || totalRows % BucketCount != 0)
        {
            throw new ArgumentException(
                $"数据行数必须为 {BucketCount} 的正倍数。",
                nameof(totalRows));
        }

        var bucket = index % BucketCount;
        var createdAtUtc = DatasetStart(referenceUtc).AddTicks(
            DatasetDuration.Ticks * index / Math.Max(1, totalRows - 1));
        var isTenantNoise = bucket >= 16;
        var status = bucket switch
        {
            <= 9 or >= 16 => JobExecutionStatuses.Pending,
            <= 11 => JobExecutionStatuses.Running,
            <= 13 => JobExecutionStatuses.Succeeded,
            _ => JobExecutionStatuses.Failed,
        };
        DateTimeOffset? nextAttemptAtUtc = bucket switch
        {
            >= 5 and <= 7 => DatasetStart(referenceUtc)
                .AddTicks(DatasetDuration.Ticks * index
                    / Math.Max(1, totalRows - 1)),
            >= 8 and <= 9 => referenceUtc
                .AddMinutes(1 + index % 1_440),
            _ => null,
        };

        return new JobsBacklogSeedRow(
            CreateExecutionId(index, referenceUtc),
            isTenantNoise ? TenantId : null,
            isTenantNoise ? TenantDefinitionId : HostDefinitionId,
            status,
            JobTriggerKinds.Manual,
            nextAttemptAtUtc,
            nextAttemptAtUtc is null ? 0 : 1,
            createdAtUtc);
    }

    public static JobsBacklogDatasetExpectation CreateExpectation(
        int rows,
        DateTimeOffset referenceUtc)
    {
        long pending = 0;
        long due = 0;
        long claimable = 0;
        long tenantNoise = 0;
        DateTimeOffset? oldestClaimable = null;
        DateTimeOffset? oldestDue = null;
        for (var index = 0; index < rows; index++)
        {
            var row = CreateRow(index, rows, referenceUtc);
            if (row.TenantId is not null)
            {
                if (row.Status == JobExecutionStatuses.Pending)
                {
                    tenantNoise++;
                }

                continue;
            }

            if (row.Status != JobExecutionStatuses.Pending)
            {
                continue;
            }

            pending++;
            var isClaimable = row.NextAttemptAtUtc is null
                || row.NextAttemptAtUtc <= referenceUtc;
            if (isClaimable)
            {
                claimable++;
                oldestClaimable = Minimum(
                    oldestClaimable,
                    row.CreatedAtUtc);
            }

            if (row.NextAttemptAtUtc is { } retryAt
                && retryAt <= referenceUtc)
            {
                due++;
                oldestDue = Minimum(oldestDue, retryAt);
            }
        }

        return new JobsBacklogDatasetExpectation(
            pending,
            due,
            claimable,
            tenantNoise,
            oldestClaimable,
            oldestDue);
    }

    private static DateTimeOffset DatasetStart(DateTimeOffset referenceUtc) =>
        referenceUtc.Subtract(DatasetDuration);

    private static DateTimeOffset Minimum(
        DateTimeOffset? current,
        DateTimeOffset candidate) =>
        current is null || candidate < current
            ? candidate
            : current.Value;

    private static Guid CreateExecutionId(
        int index,
        DateTimeOffset referenceUtc)
    {
        var timestamp = referenceUtc
            .AddMilliseconds(-(index + 1L))
            .UtcDateTime;
        return Guid.CreateVersion7(timestamp);
    }

    private static readonly TimeSpan DatasetDuration =
        TimeSpan.FromDays(30);
}
