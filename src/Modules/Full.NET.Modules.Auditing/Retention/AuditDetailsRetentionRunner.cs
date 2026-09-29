using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

internal readonly record struct AuditDetailsRetentionResult(int Cleared, int BatchesExecuted, bool MayHaveMore);

/// <summary>
/// 有界清空过期操作详情；不读取普通 Auditing:Retention 开关，失败时不伪造清理成功。
/// </summary>
internal sealed class AuditDetailsRetentionRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction commandTransaction,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<AuditDetailsRetentionResult> RunOnceAsync(
        AuditDetailsRetentionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var nowUtc = clock.UtcNow;
        var cleared = 0;
        var batches = 0;
        var lastAffected = 0;
        while (batches < options.MaxBatchesPerRun)
        {
            var affected = _provider switch
            {
                DatabaseProvider.SqlServer => await ClearSqlServerBatchAsync(
                    nowUtc, options.BatchSize, cancellationToken).ConfigureAwait(false),
                DatabaseProvider.MySql => await ClearMySqlBatchAsync(
                    nowUtc, options.BatchSize, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Unsupported database provider '{_provider}'."),
            };
            cleared += affected;
            batches++;
            lastAffected = affected;
            if (affected < options.BatchSize)
            {
                break;
            }
        }

        return new AuditDetailsRetentionResult(
            cleared,
            batches,
            batches == options.MaxBatchesPerRun && lastAffected == options.BatchSize);
    }

    private async Task<int> ClearSqlServerBatchAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
            AuditDetailsRetentionSql.ClearExpiredSqlServer,
            AuditingSqlParameters.Create(("NowUtc", nowUtc), ("BatchSize", batchSize)),
            cancellationToken).ConfigureAwait(false);
        if (affected < 0 || affected > batchSize)
        {
            throw new InvalidOperationException("Audit details cleanup exceeded its SQL Server batch limit.");
        }

        return affected;
    }

    private Task<int> ClearMySqlBatchAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        commandTransaction.ExecuteAsync(async token =>
        {
            var ids = await queryExecutor.QueryAsync<Guid>(
                AuditDetailsRetentionSql.SelectExpiredIdsMySql,
                AuditingSqlParameters.Create(("NowUtc", nowUtc), ("BatchSize", batchSize)),
                token).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return 0;
            }

            if (ids.Count > batchSize)
            {
                throw new InvalidOperationException("Audit details cleanup exceeded its MySQL batch limit.");
            }

            var affected = await commandExecutor.ExecuteAsync(
                AuditDetailsRetentionSql.ClearClaimedMySql,
                AuditingSqlParameters.Create(("Ids", ids.ToArray()), ("NowUtc", nowUtc)),
                token).ConfigureAwait(false);
            if (affected != ids.Count)
            {
                throw new InvalidOperationException("Audit details cleanup did not clear every claimed MySQL row.");
            }

            return affected;
        }, cancellationToken);
}
