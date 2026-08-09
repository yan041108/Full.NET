using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Outbox;

internal sealed class DapperEventStreamOwnershipGate(
    IQueryExecutor queryExecutor,
    IDbTransactionCoordinator transactionCoordinator,
    IOptions<DatabaseOptions> databaseOptions) : IEventStreamOwnershipGate
{
    public Task<bool> AcquireProducerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            eventType,
            schemaVersion,
            exclusive: false,
            rejectRollbackPreparing: true,
            cancellationToken);

    public Task<bool> AcquireConsumerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            eventType,
            schemaVersion,
            exclusive: false,
            rejectRollbackPreparing: false,
            cancellationToken);

    public Task<bool> AcquireOwnershipChangeAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            eventType,
            schemaVersion,
            exclusive: true,
            rejectRollbackPreparing: false,
            cancellationToken);

    private async Task<bool> AcquireAsync(
        string eventType,
        int schemaVersion,
        bool exclusive,
        bool rejectRollbackPreparing,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (!transactionCoordinator.HasTransaction)
        {
            throw new InvalidOperationException(
                "Event stream ownership gates require an active database transaction.");
        }

        var statement = (databaseOptions.Value.Provider, exclusive) switch
        {
            (DatabaseProvider.SqlServer, false) => ProducerSqlServer,
            (DatabaseProvider.SqlServer, true) => OwnershipChangeSqlServer,
            (DatabaseProvider.MySql, false) => ProducerMySql,
            (DatabaseProvider.MySql, true) => OwnershipChangeMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{databaseOptions.Value.Provider}' is not supported."),
        };
        var owner = await queryExecutor.QuerySingleOrDefaultAsync<int?>(
            statement,
            new { MessageType = eventType, SchemaVersion = schemaVersion },
            cancellationToken).ConfigureAwait(false);
        if (rejectRollbackPreparing && owner == -1)
        {
            throw new EventDeliveryProducerFencedException(eventType, schemaVersion);
        }

        return owner.HasValue;
    }

    private static readonly SqlStatement ProducerSqlServer = new(
        "messaging.stream_ownership_gate.producer.sql_server",
        """
        SELECT CASE WHEN RollbackState = 1 THEN -1 ELSE CurrentOwner END
        FROM fn_messaging_stream_ownership WITH (HOLDLOCK)
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement OwnershipChangeSqlServer = new(
        "messaging.stream_ownership_gate.change.sql_server",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership WITH (UPDLOCK, HOLDLOCK)
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement ProducerMySql = new(
        "messaging.stream_ownership_gate.producer.my_sql",
        """
        SELECT CASE WHEN RollbackState = 1 THEN -1 ELSE CurrentOwner END
        FROM fn_messaging_stream_ownership
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        FOR SHARE
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement OwnershipChangeMySql = new(
        "messaging.stream_ownership_gate.change.my_sql",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        FOR UPDATE
        """,
        SqlDataScope.Global);
}
