using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Persistence;

internal sealed class EventStreamOwnershipStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IEventStreamOwnershipStore
{
    public async Task<EventStreamOwnershipRecord?> FindAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<EventStreamOwnershipPersistenceRow>(
                EventStreamOwnershipSql.FindByStream,
                new { MessageType = messageType, SchemaVersion = schemaVersion },
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : EventStreamOwnershipMapper.ToRecord(row);
    }

    public async Task<IReadOnlyList<EventStreamOwnershipRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<EventStreamOwnershipPersistenceRow>(
                EventStreamOwnershipSql.ListAll,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(EventStreamOwnershipMapper.ToRecord).ToArray();
    }

    public async Task UpsertAsync(
        EventStreamOwnershipRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var persistenceRow = EventStreamOwnershipMapper.ToPersistenceRow(record);
        var existing = await FindAsync(record.MessageType, record.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        var affected = existing is null
            ? await commandExecutor.ExecuteAsync(
                EventStreamOwnershipSql.Insert,
                persistenceRow,
                cancellationToken).ConfigureAwait(false)
            : await commandExecutor.ExecuteAsync(
                EventStreamOwnershipSql.Update,
                persistenceRow,
                cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Stream ownership upsert affected {affected} rows instead of 1.");
        }
    }

    internal async Task<OutboxStreamCutoffRecord?> FindLastOutboxEventAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => EventStreamOwnershipSql.FindLastOutboxEventByStreamSqlServer,
            DatabaseProvider.MySql => EventStreamOwnershipSql.FindLastOutboxEventByStreamMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        return await queryExecutor
            .QuerySingleOrDefaultAsync<OutboxStreamCutoffRecord>(
                statement,
                new { MessageType = messageType, SchemaVersion = schemaVersion },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
