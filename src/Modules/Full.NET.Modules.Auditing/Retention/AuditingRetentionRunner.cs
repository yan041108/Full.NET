using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

internal enum AuditingRetentionCategory
{
    Access,
    Operation,
    Exception,
    Outbound,
}

internal sealed record AuditingRetentionResult(
    int AccessDeleted,
    int OperationDeleted,
    int ExceptionDeleted,
    int OutboundDeleted,
    int BatchesExecuted)
{
    public static AuditingRetentionResult Empty { get; } = new(0, 0, 0, 0, 0);

    public int TotalDeleted =>
        AccessDeleted + OperationDeleted + ExceptionDeleted + OutboundDeleted;
}

internal sealed class AuditingRetentionRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction commandTransaction,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    private static readonly AuditingRetentionCategory[] Categories =
    [
        AuditingRetentionCategory.Access,
        AuditingRetentionCategory.Operation,
        AuditingRetentionCategory.Exception,
        AuditingRetentionCategory.Outbound,
    ];

    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<AuditingRetentionResult> RunOnceAsync(
        AuditingRetentionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return AuditingRetentionResult.Empty;
        }

        var now = clock.UtcNow;
        var cutoffs = new[]
        {
            now.AddDays(-options.AccessRetentionDays),
            now.AddDays(-options.OperationRetentionDays),
            now.AddDays(-options.ExceptionRetentionDays),
            now.AddDays(-options.OutboundRetentionDays),
        };
        var active = new[] { true, true, true, true };
        var deleted = new int[4];
        var batches = 0;

        while (batches < options.MaxBatchesPerRun && active.Any(value => value))
        {
            foreach (var category in Categories)
            {
                if (batches >= options.MaxBatchesPerRun)
                {
                    break;
                }

                var index = (int)category;
                if (!active[index])
                {
                    continue;
                }

                var affectedRows = await DeleteBatchAsync(
                        category,
                        cutoffs[index],
                        options.BatchSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                deleted[index] += affectedRows;
                batches++;

                // 不足一批表示该类别已追平当前截止时间，本轮不再重复探测。
                if (affectedRows < options.BatchSize)
                {
                    active[index] = false;
                }
            }
        }

        return new AuditingRetentionResult(
            deleted[(int)AuditingRetentionCategory.Access],
            deleted[(int)AuditingRetentionCategory.Operation],
            deleted[(int)AuditingRetentionCategory.Exception],
            deleted[(int)AuditingRetentionCategory.Outbound],
            batches);
    }

    private Task<int> DeleteBatchAsync(
        AuditingRetentionCategory category,
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        _provider switch
        {
            DatabaseProvider.SqlServer => DeleteSqlServerBatchAsync(
                category,
                cutoffUtc,
                batchSize,
                cancellationToken),
            DatabaseProvider.MySql => DeleteMySqlBatchAsync(
                category,
                cutoffUtc,
                batchSize,
                cancellationToken),
            _ => throw new NotSupportedException(
                $"Unsupported database provider '{_provider}'."),
        };

    private async Task<int> DeleteSqlServerBatchAsync(
        AuditingRetentionCategory category,
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                AuditingRetentionSql.GetSqlServerDelete(category),
                new { CutoffUtc = cutoffUtc, BatchSize = batchSize },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureAffectedRowsWithinBatch(affectedRows, batchSize);
        return affectedRows;
    }

    private Task<int> DeleteMySqlBatchAsync(
        AuditingRetentionCategory category,
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        commandTransaction.ExecuteAsync(
            async transactionToken =>
            {
                var ids = await queryExecutor.QueryAsync<Guid>(
                        AuditingRetentionSql.GetMySqlSelect(category),
                        new { CutoffUtc = cutoffUtc, BatchSize = batchSize },
                        transactionToken)
                    .ConfigureAwait(false);
                if (ids.Count == 0)
                {
                    return 0;
                }

                var claimedIds = ids.ToArray();
                var affectedRows = await commandExecutor.ExecuteAsync(
                        AuditingRetentionSql.GetMySqlDelete(category),
                        new { Ids = claimedIds },
                        transactionToken)
                    .ConfigureAwait(false);
                if (affectedRows != claimedIds.Length)
                {
                    throw new InvalidOperationException(
                        "Audit retention did not delete every claimed MySQL row.");
                }

                return affectedRows;
            },
            cancellationToken);

    private static void EnsureAffectedRowsWithinBatch(
        int affectedRows,
        int batchSize)
    {
        if (affectedRows < 0 || affectedRows > batchSize)
        {
            throw new InvalidOperationException(
                "Audit retention affected rows outside the configured batch.");
        }
    }
}
