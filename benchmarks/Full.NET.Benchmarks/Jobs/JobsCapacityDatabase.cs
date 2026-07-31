using System.Data.Common;
using System.Text;
using Dapper;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsCapacityDatabaseState(
    long PendingExecutions,
    long RunningExecutions,
    long SucceededExecutions,
    long FailedExecutions,
    long TerminalExecutionsWithLease,
    long AttemptCountGreaterThanOne,
    JobsCapacityStatistics? HandlerLatency,
    JobsCapacityStatistics? QueueLatency)
{
    public long TerminalExecutions =>
        SucceededExecutions + FailedExecutions;
}

public sealed class JobsCapacityDatabase : IAsyncDisposable
{
    private const int InsertBatchSize = 200;
    private readonly MixedLoadDatabase _database;

    private JobsCapacityDatabase(MixedLoadDatabase database)
    {
        _database = database;
    }

    public string Provider => _database.Provider.ToString().ToLowerInvariant();

    public string ConnectionString => _database.ConnectionString;

    public string ContainerImage => _database.ContainerImage;

    public string ContainerId => _database.ContainerId;

    public string DatabaseVersion => _database.DatabaseVersion;

    internal MixedLoadDatabase MixedLoad => _database;

    public static async Task<JobsCapacityDatabase> StartAsync(
        string provider,
        string poolName,
        CancellationToken cancellationToken) =>
        new(await MixedLoadDatabase.StartAsync(
            provider,
            poolName,
            cancellationToken));

    public async Task ResetAndSeedAsync(
        int jobCount,
        int handlerKeyCount,
        int failingHandlerKeyCount,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(handlerKeyCount);
        if (failingHandlerKeyCount < 0
            || failingHandlerKeyCount >= handlerKeyCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failingHandlerKeyCount));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM fn_jobs_execution;
            DELETE FROM fn_jobs_definition;
            """,
            transaction: transaction,
            cancellationToken: cancellationToken));
        var definitionIds = Enumerable.Range(0, handlerKeyCount)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        var actorId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO fn_jobs_definition
                (Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
                 CreatedAtUtc, UpdatedAtUtc, CreatedByUserId,
                 UpdatedByUserId, Version)
            VALUES
                (@Id, NULL, @JobKey, @DisplayName, NULL, @IsEnabled,
                 @CreatedAtUtc, NULL, @CreatedByUserId, NULL, 1)
            """,
            definitionIds.Select((id, index) => new
            {
                Id = id,
                JobKey = CreateJobKey(index, failingHandlerKeyCount),
                DisplayName = $"Jobs capacity {index}",
                IsEnabled = true,
                CreatedAtUtc = ToDatabaseTimestamp(createdAtUtc),
                CreatedByUserId = actorId,
            }),
            transaction,
            cancellationToken: cancellationToken));
        for (var start = 0; start < jobCount; start += InsertBatchSize)
        {
            var count = Math.Min(InsertBatchSize, jobCount - start);
            var sql = new StringBuilder(
                """
                INSERT INTO fn_jobs_execution
                    (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                     ErrorMessage, StartedAtUtc, FinishedAtUtc,
                     LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
                     AttemptCount, CreatedAtUtc)
                VALUES
                """);
            var parameters = new DynamicParameters();
            for (var offset = 0; offset < count; offset++)
            {
                var parameterIndex = offset;
                sql.Append(offset == 0 ? Environment.NewLine : ",").Append(
                    $"(@Id{parameterIndex}, NULL, @Definition{parameterIndex}, "
                    + $"@Status{parameterIndex}, @Trigger{parameterIndex}, "
                    + "NULL, NULL, NULL, NULL, NULL, NULL, 0, "
                    + $"@Created{parameterIndex})");
                var absoluteIndex = start + offset;
                parameters.Add(
                    $"Id{parameterIndex}",
                    Guid.CreateVersion7());
                parameters.Add(
                    $"Definition{parameterIndex}",
                    definitionIds[absoluteIndex % definitionIds.Length]);
                parameters.Add(
                    $"Status{parameterIndex}",
                    JobExecutionStatuses.Pending);
                parameters.Add(
                    $"Trigger{parameterIndex}",
                    JobTriggerKinds.Manual);
                parameters.Add(
                    $"Created{parameterIndex}",
                    ToDatabaseTimestamp(
                        createdAtUtc.AddTicks(absoluteIndex * 10L)));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                sql.ToString(),
                parameters,
                transaction,
                commandTimeout: 300,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<JobsCapacityDatabaseState> ReadStateAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var counts = await connection.QuerySingleAsync<CapacityCountRow>(
            new CommandDefinition(
                Provider == "sqlserver"
                    ? SqlServerCountSql
                    : MySqlCountSql,
                cancellationToken: cancellationToken));
        var latencyRows =
            (await connection.QueryAsync<CapacityLatencyRow>(
                new CommandDefinition(
                    Provider == "sqlserver"
                        ? SqlServerLatencySql
                        : MySqlLatencySql,
                    cancellationToken: cancellationToken)))
            .ToArray();
        return new JobsCapacityDatabaseState(
            counts.PendingExecutions,
            counts.RunningExecutions,
            counts.SucceededExecutions,
            counts.FailedExecutions,
            counts.TerminalExecutionsWithLease,
            counts.AttemptCountGreaterThanOne,
            latencyRows.Length == 0
                ? null
                : JobsCapacityStatistics.Calculate(
                    latencyRows.Select(row =>
                        row.HandlerMilliseconds).ToArray()),
            latencyRows.Length == 0
                ? null
                : JobsCapacityStatistics.Calculate(
                    latencyRows.Select(row =>
                        row.QueueMilliseconds).ToArray()));
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private DbConnection CreateConnection() =>
        Provider == "sqlserver"
            ? new SqlConnection(ConnectionString)
            : new MySqlConnection(ConnectionString);

    private object ToDatabaseTimestamp(DateTimeOffset timestamp) =>
        Provider == "sqlserver"
            ? timestamp
            : timestamp.UtcDateTime;

    private static string CreateJobKey(
        int index,
        int failingHandlerKeyCount) =>
        index < failingHandlerKeyCount
            ? $"jobs.benchmark.capacity.failure.{index}"
            : "jobs.benchmark.capacity.success."
                + $"{index - failingHandlerKeyCount}";

    private const string SqlServerCountSql =
        """
        SELECT
            COUNT_BIG(CASE WHEN Status = 'pending' THEN 1 END)
                AS PendingExecutions,
            COUNT_BIG(CASE WHEN Status = 'running' THEN 1 END)
                AS RunningExecutions,
            COUNT_BIG(CASE WHEN Status = 'succeeded' THEN 1 END)
                AS SucceededExecutions,
            COUNT_BIG(CASE WHEN Status = 'failed' THEN 1 END)
                AS FailedExecutions,
            COUNT_BIG(
                CASE WHEN Status IN ('succeeded', 'failed')
                           AND (LeaseId IS NOT NULL
                                OR LeaseExpiresAtUtc IS NOT NULL)
                     THEN 1 END)
                AS TerminalExecutionsWithLease,
            COUNT_BIG(CASE WHEN AttemptCount > 1 THEN 1 END)
                AS AttemptCountGreaterThanOne
        FROM fn_jobs_execution
        """;

    private const string MySqlCountSql =
        """
        SELECT
            COUNT(CASE WHEN Status = 'pending' THEN 1 END)
                AS PendingExecutions,
            COUNT(CASE WHEN Status = 'running' THEN 1 END)
                AS RunningExecutions,
            COUNT(CASE WHEN Status = 'succeeded' THEN 1 END)
                AS SucceededExecutions,
            COUNT(CASE WHEN Status = 'failed' THEN 1 END)
                AS FailedExecutions,
            COUNT(
                CASE WHEN Status IN ('succeeded', 'failed')
                           AND (LeaseId IS NOT NULL
                                OR LeaseExpiresAtUtc IS NOT NULL)
                     THEN 1 END)
                AS TerminalExecutionsWithLease,
            COUNT(CASE WHEN AttemptCount > 1 THEN 1 END)
                AS AttemptCountGreaterThanOne
        FROM fn_jobs_execution
        """;

    private const string SqlServerLatencySql =
        """
        SELECT
            CAST(DATEDIFF_BIG(
                MICROSECOND,
                CreatedAtUtc,
                StartedAtUtc) AS float) / 1000.0 AS QueueMilliseconds,
            CAST(DATEDIFF_BIG(
                MICROSECOND,
                StartedAtUtc,
                FinishedAtUtc) AS float) / 1000.0 AS HandlerMilliseconds
        FROM fn_jobs_execution
        WHERE Status IN ('succeeded', 'failed')
          AND StartedAtUtc IS NOT NULL
          AND FinishedAtUtc IS NOT NULL
        """;

    private const string MySqlLatencySql =
        """
        SELECT
            TIMESTAMPDIFF(
                MICROSECOND,
                CreatedAtUtc,
                StartedAtUtc) / 1000.0 AS QueueMilliseconds,
            TIMESTAMPDIFF(
                MICROSECOND,
                StartedAtUtc,
                FinishedAtUtc) / 1000.0 AS HandlerMilliseconds
        FROM fn_jobs_execution
        WHERE Status IN ('succeeded', 'failed')
          AND StartedAtUtc IS NOT NULL
          AND FinishedAtUtc IS NOT NULL
        """;

    private sealed class CapacityCountRow
    {
        public long PendingExecutions { get; set; }

        public long RunningExecutions { get; set; }

        public long SucceededExecutions { get; set; }

        public long FailedExecutions { get; set; }

        public long TerminalExecutionsWithLease { get; set; }

        public long AttemptCountGreaterThanOne { get; set; }
    }

    private sealed class CapacityLatencyRow
    {
        public double QueueMilliseconds { get; set; }

        public double HandlerMilliseconds { get; set; }
    }
}
