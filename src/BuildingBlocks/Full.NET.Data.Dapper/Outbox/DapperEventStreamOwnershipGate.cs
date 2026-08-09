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

    public async Task<bool> AcquireConsumerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var fence = await AcquireConsumerFenceAsync(
                eventType,
                schemaVersion,
                cancellationToken)
            .ConfigureAwait(false);
        return fence.OwnershipExists;
    }

    public async Task<EventStreamConsumerFenceResult> AcquireConsumerFenceAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateArgumentsAndTransaction(eventType, schemaVersion);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ConsumerSqlServer,
            DatabaseProvider.MySql => ConsumerMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{databaseOptions.Value.Provider}' is not supported."),
        };
        var owner = await queryExecutor.QuerySingleOrDefaultAsync<int?>(
            statement,
            new { MessageType = eventType, SchemaVersion = schemaVersion },
            cancellationToken).ConfigureAwait(false);
        if (!owner.HasValue)
        {
            return EventStreamConsumerFenceResult.Missing;
        }

        if (!Enum.IsDefined(typeof(EventDeliveryOwner), owner.Value))
        {
            throw new InvalidOperationException(
                $"Event stream ownership row contains unsupported owner '{owner.Value}'.");
        }

        return EventStreamConsumerFenceResult.Acquired((EventDeliveryOwner)owner.Value);
    }

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
        ValidateArgumentsAndTransaction(eventType, schemaVersion);

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

    private void ValidateArgumentsAndTransaction(string eventType, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (!transactionCoordinator.HasTransaction)
        {
            throw new InvalidOperationException(
                "Event stream ownership gates require an active database transaction.");
        }
    }

    private static readonly SqlStatement ConsumerSqlServer = new(
        "messaging.stream_ownership_gate.consumer.sql_server",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership WITH (HOLDLOCK)
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement ConsumerMySql = new(
        "messaging.stream_ownership_gate.consumer.my_sql",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        FOR SHARE
        """,
        SqlDataScope.Global);

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
