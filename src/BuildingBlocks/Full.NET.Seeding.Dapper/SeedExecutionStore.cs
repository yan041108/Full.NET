using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Seeding.Dapper;

/// <summary>写入 fn_seed_run / fn_seed_run_item 审计行；使用显式 SQL 与 Dapper。</summary>
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
            new Dictionary<string, object?>
            {
                ["RunId"] = audit.RunId,
                ["Profile"] = audit.Profile,
                ["EnvironmentName"] = audit.EnvironmentName,
                ["Status"] = SeedExecutionStatuses.Running,
                ["ApplicationVersion"] = audit.ApplicationVersion,
                ["CorrelationId"] = audit.CorrelationId,
                ["StartedAt"] = audit.StartedAtUtc.UtcDateTime,
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
            new Dictionary<string, object?>
            {
                ["RunId"] = audit.RunId,
                ["Status"] = audit.Status,
                ["ErrorCode"] = audit.ErrorCode,
                ["CompletedAt"] = audit.CompletedAtUtc.UtcDateTime,
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
            new Dictionary<string, object?>
            {
                ["RunId"] = audit.RunId,
                ["Contributor"] = audit.Contributor,
                ["ContributorVersion"] = audit.ContributorVersion,
                ["Status"] = SeedExecutionStatuses.Running,
                ["StartedAt"] = audit.StartedAtUtc.UtcDateTime,
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
            new Dictionary<string, object?>
            {
                ["RunId"] = audit.RunId,
                ["Contributor"] = audit.Contributor,
                ["Status"] = audit.Status,
                ["CreatedCount"] = audit.CreatedCount,
                ["UpdatedCount"] = audit.UpdatedCount,
                ["SkippedCount"] = audit.SkippedCount,
                ["ErrorCode"] = audit.ErrorCode,
                ["CompletedAt"] = audit.CompletedAtUtc.UtcDateTime,
            },
            cancellationToken);

    private async Task ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = SeedDbConnectionFactory.Create(_databaseOptions);
        await connection.OpenAsync(cancellationToken);
        // 显式绑定审计参数，禁止以 object 擦除匿名类型后回退反射生成参数访问器。
        var bound = new DynamicParameters();
        foreach (var parameter in parameters) bound.Add(parameter.Key, parameter.Value);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            bound,
            commandTimeout: _databaseOptions.CommandTimeoutSeconds,
            cancellationToken: cancellationToken));
    }
}
