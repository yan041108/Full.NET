using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Seeding.Dapper;

internal sealed class SeedExecutionStore(IOptions<DatabaseOptions> databaseOptions)
    : ISeedExecutionStore
{
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;

    public Task StartRunAsync(SeedRunAuditStart audit, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO fn_seed_run
                (Id, Profile, EnvironmentName, Status, ApplicationVersion,
                 CorrelationId, StartedAt, CompletedAt, ErrorCode)
            VALUES
                (@RunId, @Profile, @EnvironmentName, @Status, @ApplicationVersion,
                 @CorrelationId, @StartedAt, NULL, NULL);
            """,
            new
            {
                audit.RunId,
                audit.Profile,
                audit.EnvironmentName,
                Status = SeedExecutionStatuses.Running,
                audit.ApplicationVersion,
                audit.CorrelationId,
                StartedAt = audit.StartedAtUtc.UtcDateTime,
            },
            cancellationToken);

    public Task CompleteRunAsync(
        SeedRunAuditCompletion audit,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE fn_seed_run
            SET Status = @Status,
                CompletedAt = @CompletedAt,
                ErrorCode = @ErrorCode
            WHERE Id = @RunId;
            """,
            new
            {
                audit.RunId,
                audit.Status,
                audit.ErrorCode,
                CompletedAt = audit.CompletedAtUtc.UtcDateTime,
            },
            cancellationToken);

    public Task StartItemAsync(
        SeedRunItemAuditStart audit,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO fn_seed_run_item
                (RunId, Contributor, ContributorVersion, Status,
                 CreatedCount, UpdatedCount, SkippedCount,
                 StartedAt, CompletedAt, ErrorCode)
            VALUES
                (@RunId, @Contributor, @ContributorVersion, @Status,
                 0, 0, 0, @StartedAt, NULL, NULL);
            """,
            new
            {
                audit.RunId,
                audit.Contributor,
                audit.ContributorVersion,
                Status = SeedExecutionStatuses.Running,
                StartedAt = audit.StartedAtUtc.UtcDateTime,
            },
            cancellationToken);

    public Task CompleteItemAsync(
        SeedRunItemAuditCompletion audit,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE fn_seed_run_item
            SET Status = @Status,
                CreatedCount = @CreatedCount,
                UpdatedCount = @UpdatedCount,
                SkippedCount = @SkippedCount,
                CompletedAt = @CompletedAt,
                ErrorCode = @ErrorCode
            WHERE RunId = @RunId AND Contributor = @Contributor;
            """,
            new
            {
                audit.RunId,
                audit.Contributor,
                audit.Status,
                audit.CreatedCount,
                audit.UpdatedCount,
                audit.SkippedCount,
                audit.ErrorCode,
                CompletedAt = audit.CompletedAtUtc.UtcDateTime,
            },
            cancellationToken);

    private async Task ExecuteAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = SeedDbConnectionFactory.Create(_databaseOptions);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _databaseOptions.CommandTimeoutSeconds,
            cancellationToken: cancellationToken));
    }
}
