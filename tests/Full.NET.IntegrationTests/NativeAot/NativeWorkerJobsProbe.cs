using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>为原生 Worker 写入确定性 Ping Job，并按执行标识读取终态。</summary>
internal static class NativeWorkerJobsProbe
{
    private const string InsertDefinitionSql =
        """
        INSERT INTO fn_jobs_definition
            (Id, TenantId, JobKey, HandlerKind, ArgsJson,
             DisplayName, Description, GroupName, IsEnabled,
             AllowConcurrentExecutions, CreatedAtUtc, UpdatedAtUtc,
             CreatedByUserId, UpdatedByUserId, Version)
        VALUES
            (@Id, NULL, @JobKey, @HandlerKind, NULL,
             @DisplayName, NULL, @GroupName, @IsEnabled,
             @AllowConcurrentExecutions, @CreatedAtUtc, NULL,
             @CreatedByUserId, NULL, 1)
        """;

    private const string InsertExecutionSql =
        """
        INSERT INTO fn_jobs_execution
            (Id, TenantId, JobDefinitionId, JobScheduleId,
             Status, TriggerKind, ScheduledForUtc,
             ErrorMessage, StartedAtUtc, FinishedAtUtc,
             LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
             AttemptCount, CreatedAtUtc)
        VALUES
            (@Id, NULL, @JobDefinitionId, NULL,
             @Status, @TriggerKind, NULL,
             NULL, NULL, NULL,
             NULL, NULL, NULL,
             0, @CreatedAtUtc)
        """;

    private const string SelectStateSql =
        """
        SELECT Id, Status, AttemptCount, ErrorMessage,
               CASE WHEN StartedAtUtc IS NULL THEN 0 ELSE 1 END AS IsStarted,
               CASE WHEN FinishedAtUtc IS NULL THEN 0 ELSE 1 END AS IsFinished,
               CASE WHEN StartedAtUtc IS NOT NULL
                          AND FinishedAtUtc IS NOT NULL
                          AND FinishedAtUtc >= StartedAtUtc
                    THEN 1 ELSE 0 END AS IsChronological,
               CASE WHEN LeaseId IS NULL AND LeaseExpiresAtUtc IS NULL
                    THEN 1 ELSE 0 END AS IsLeaseReleased,
               CASE WHEN NextAttemptAtUtc IS NULL THEN 1 ELSE 0 END AS IsRetryCleared
        FROM fn_jobs_execution
        WHERE Id = @Id AND TenantId IS NULL
        """;

    public static async Task<Guid> EnqueuePingAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        var createdByUserId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var databaseNow = provider == DatabaseProvider.MySql
            ? (object)now.UtcDateTime
            : now;

        await using var connection = CreateConnection(provider, connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertDefinitionSql,
                    new
                    {
                        Id = definitionId,
                        JobKey = $"native-aot-ping-{definitionId:N}",
                        HandlerKind = JobHandlerKinds.Ping,
                        DisplayName = "Native Worker Ping Probe",
                        GroupName = "native-aot",
                        IsEnabled = true,
                        AllowConcurrentExecutions = false,
                        CreatedAtUtc = databaseNow,
                        CreatedByUserId = createdByUserId,
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertExecutionSql,
                    new
                    {
                        Id = executionId,
                        JobDefinitionId = definitionId,
                        Status = JobExecutionStatuses.Pending,
                        TriggerKind = JobTriggerKinds.Manual,
                        CreatedAtUtc = databaseNow,
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return executionId;
    }

    public static async Task<NativeWorkerJobExecutionState> WaitForTerminalAsync(
        DatabaseProvider provider,
        string connectionString,
        Guid executionId,
        TimeSpan timeout,
        string logFilePath,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        NativeWorkerJobExecutionState? lastState = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastState = await ReadStateAsync(
                    provider,
                    connectionString,
                    executionId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (lastState.IsTerminal)
            {
                return lastState;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Native Worker 未在 {timeout} 内写入 Jobs 终态；最后状态为 "
            + $"'{lastState?.Status ?? "missing"}'，尝试次数 "
            + $"{lastState?.AttemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}。"
            + $"日志：{logFilePath}");
    }

    private static async Task<NativeWorkerJobExecutionState> ReadStateAsync(
        DatabaseProvider provider,
        string connectionString,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(provider, connectionString);
        return await connection.QuerySingleOrDefaultAsync<NativeWorkerJobExecutionState>(
                new CommandDefinition(
                    SelectStateSql,
                    new { Id = executionId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Native Worker Jobs 探针执行 '{executionId:D}' 在终态检查前丢失。");
    }

    private static DbConnection CreateConnection(
        DatabaseProvider provider,
        string connectionString) => provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'."),
        };
}

internal sealed class NativeWorkerJobExecutionState
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public string? ErrorMessage { get; init; }

    public long IsStarted { get; init; }

    public long IsFinished { get; init; }

    public long IsChronological { get; init; }

    public long IsLeaseReleased { get; init; }

    public long IsRetryCleared { get; init; }

    public bool IsTerminal =>
        string.Equals(Status, JobExecutionStatuses.Succeeded, StringComparison.Ordinal)
        || string.Equals(Status, JobExecutionStatuses.Failed, StringComparison.Ordinal);
}
