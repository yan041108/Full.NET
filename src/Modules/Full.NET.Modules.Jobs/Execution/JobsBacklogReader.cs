using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>通过单次只读聚合查询获取 Host Jobs 积压快照。</summary>
internal sealed class JobsBacklogReader(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<JobsBacklogSnapshot> ReadAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            ObservedAtUtc = observedAtUtc,
            PendingStatus = JobExecutionStatuses.Pending,
        };
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var row = await queryExecutor
                .QuerySingleOrDefaultAsync<JobsBacklogSqlServerRow>(
                    JobSql.ReadBacklogSqlServer,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            return new JobsBacklogSnapshot(
                row?.PendingCount ?? 0,
                row?.OldestClaimableCreatedAtUtc,
                row?.DueRetryCount ?? 0,
                row?.OldestDueRetryAtUtc);
        }

        if (databaseOptions.Value.Provider == DatabaseProvider.MySql)
        {
            var row = await queryExecutor
                .QuerySingleOrDefaultAsync<JobsBacklogMySqlRow>(
                    JobSql.ReadBacklogMySql,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            return new JobsBacklogSnapshot(
                row?.PendingCount ?? 0,
                AsUtc(row?.OldestClaimableCreatedAtUtc),
                row?.DueRetryCount ?? 0,
                AsUtc(row?.OldestDueRetryAtUtc));
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{databaseOptions.Value.Provider}'.");
    }

    private static DateTimeOffset? AsUtc(DateTime? value) =>
        value is { } dateTime
            ? new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
            : null;
}

internal sealed class JobsBacklogSqlServerRow
{
    public long PendingCount { get; init; }

    public DateTimeOffset? OldestClaimableCreatedAtUtc { get; init; }

    public long DueRetryCount { get; init; }

    public DateTimeOffset? OldestDueRetryAtUtc { get; init; }
}

internal sealed class JobsBacklogMySqlRow
{
    public long PendingCount { get; init; }

    public DateTime? OldestClaimableCreatedAtUtc { get; init; }

    public long DueRetryCount { get; init; }

    public DateTime? OldestDueRetryAtUtc { get; init; }
}
