using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

internal readonly record struct AuditDetailsCleanupCheckpointSnapshot(
    DateTimeOffset LastSuccessfulCleanupAtUtc,
    DateTimeOffset? OldestExpiredAtUtc);

internal sealed class AuditDetailsCleanupCheckpointSqlServerRow
{
    public DateTimeOffset LastSuccessfulCleanupAtUtc { get; init; }

    public DateTimeOffset? OldestExpiredAtUtc { get; init; }
}

internal sealed class AuditDetailsCleanupCheckpointMySqlRow
{
    public DateTime LastSuccessfulCleanupAtUtc { get; init; }

    public DateTime? OldestExpiredAtUtc { get; init; }
}

internal interface IAuditDetailsCleanupCheckpointReader
{
    Task<AuditDetailsCleanupCheckpointSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>仅在一次清理及积压读取均成功后，刷新 Auditing 自有的共享检查点。</summary>
internal sealed class AuditDetailsCleanupCheckpointStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction commandTransaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions) : IAuditDetailsCleanupCheckpointReader
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    public async Task<AuditDetailsCleanupCheckpointSnapshot?> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (_provider == DatabaseProvider.SqlServer)
        {
            var row = await queryExecutor.QuerySingleOrDefaultAsync<
                AuditDetailsCleanupCheckpointSqlServerRow>(
                AuditDetailsCleanupCheckpointSql.ReadSqlServer,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return row is null
                ? null
                : new AuditDetailsCleanupCheckpointSnapshot(
                    row.LastSuccessfulCleanupAtUtc,
                    row.OldestExpiredAtUtc);
        }

        if (_provider == DatabaseProvider.MySql)
        {
            var row = await queryExecutor.QuerySingleOrDefaultAsync<
                AuditDetailsCleanupCheckpointMySqlRow>(
                AuditDetailsCleanupCheckpointSql.ReadMySql,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return row is null
                ? null
                : new AuditDetailsCleanupCheckpointSnapshot(
                    AsUtc(row.LastSuccessfulCleanupAtUtc),
                    row.OldestExpiredAtUtc is { } oldest ? AsUtc(oldest) : null);
        }

        throw new NotSupportedException($"Unsupported database provider '{_provider}'.");
    }

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public async Task<AuditDetailsCleanupCheckpointSnapshot> RecordSuccessfulPassAsync(
        CancellationToken cancellationToken)
    {
        var observedAtUtc = clock.UtcNow.ToUniversalTime();
        DateTimeOffset? oldestExpiredAtUtc;
        switch (_provider)
        {
            case DatabaseProvider.SqlServer:
                oldestExpiredAtUtc = await queryExecutor.QuerySingleOrDefaultAsync<DateTimeOffset?>(
                    AuditDetailsCleanupCheckpointSql.OldestExpiredSqlServer,
                    AuditingSqlParameters.Create(("ObservedAtUtc", observedAtUtc)),
                    cancellationToken).ConfigureAwait(false);
                break;
            case DatabaseProvider.MySql:
                var oldestMySql = await queryExecutor.QuerySingleOrDefaultAsync<DateTime?>(
                    AuditDetailsCleanupCheckpointSql.OldestExpiredMySql,
                    AuditingSqlParameters.Create(("ObservedAtUtc", observedAtUtc.UtcDateTime)),
                    cancellationToken).ConfigureAwait(false);
                oldestExpiredAtUtc = oldestMySql is { } value
                    ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                    : null;
                break;
            default:
                throw new NotSupportedException($"Unsupported database provider '{_provider}'.");
        }

        var parameters = AuditingSqlParameters.Create(
            ("Id", idGenerator.NewId()),
            ("ObservedAtUtc", _provider == DatabaseProvider.MySql
                ? observedAtUtc.UtcDateTime : observedAtUtc),
            ("OldestExpiredAtUtc", _provider == DatabaseProvider.MySql
                ? oldestExpiredAtUtc?.UtcDateTime : oldestExpiredAtUtc));
        if (_provider == DatabaseProvider.SqlServer)
        {
            await commandTransaction.ExecuteAsync(
                token => commandExecutor.ExecuteAsync(
                    AuditDetailsCleanupCheckpointSql.UpsertSqlServer,
                    parameters,
                    token),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await commandExecutor.ExecuteAsync(
                AuditDetailsCleanupCheckpointSql.UpsertMySql,
                parameters,
                cancellationToken).ConfigureAwait(false);
        }

        return new AuditDetailsCleanupCheckpointSnapshot(observedAtUtc, oldestExpiredAtUtc);
    }
}
