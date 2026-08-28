using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;
using DapperSqlParameters = Full.NET.Data.Dapper.DapperSqlParameters;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 从持久化回退准备与数据库 CDC 位点读取 producer fence。
/// </summary>
internal sealed class DapperEventDeliveryProducerFencePositionReader(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IEventDeliveryProducerFencePositionReader
{
    public async Task<EventDeliveryProducerFenceSnapshot?> TryReadAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        var preparation = await queryExecutor
            .QuerySingleOrDefaultAsync<RollbackPreparationRow>(
                ProducerFenceSql.FindActivePreparation,
                DapperSqlParameters.Create(
                    ("MessageType", eventType),
                    ("SchemaVersion", schemaVersion),
                    ("RollbackGeneration", rollbackGeneration)),
                cancellationToken)
            .ConfigureAwait(false);
        if (preparation is null)
        {
            return null;
        }

        var lastEvent = await queryExecutor
            .QuerySingleOrDefaultAsync<LastOutboxEventRow>(
                databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer => ProducerFenceSql.FindLastOutboxEventSqlServer,
                    DatabaseProvider.MySql => ProducerFenceSql.FindLastOutboxEventMySql,
                    _ => throw new NotSupportedException(
                        $"Database provider '{databaseOptions.Value.Provider}' is not supported."),
                },
                DapperSqlParameters.Create(
                    ("MessageType", eventType),
                    ("SchemaVersion", schemaVersion)),
                cancellationToken)
            .ConfigureAwait(false);

        var observedAtUtc = DateTimeOffset.UtcNow;
        var position = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => await ReadSqlServerPositionAsync(
                    lastEvent?.CutoffEventId,
                    cancellationToken)
                .ConfigureAwait(false),
            DatabaseProvider.MySql => await ReadMySqlPositionAsync(
                    lastEvent?.CutoffEventId,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException(
                $"Database provider '{databaseOptions.Value.Provider}' is not supported."),
        };

        return new EventDeliveryProducerFenceSnapshot(
            rollbackGeneration,
            position,
            lastEvent?.CutoffEventId,
            observedAtUtc);
    }

    private async Task<CdcDeliveryPosition> ReadMySqlPositionAsync(
        Guid? lastEventId,
        CancellationToken cancellationToken)
    {
        var status = await queryExecutor
            .QuerySingleOrDefaultAsync<MySqlMasterStatusRow>(
                ProducerFenceSql.ShowMasterStatus,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (status is null
            || string.IsNullOrWhiteSpace(status.File)
            || status.Position is null)
        {
            throw new InvalidOperationException(
                "MySQL master status is unavailable for producer fence capture.");
        }

        return CdcDeliveryPosition.ForMySql(
            lastEventId,
            status.File,
            status.Position.Value);
    }

    private async Task<CdcDeliveryPosition> ReadSqlServerPositionAsync(
        Guid? lastEventId,
        CancellationToken cancellationToken)
    {
        var lsn = await queryExecutor
            .QuerySingleOrDefaultAsync<SqlServerMaxLsnRow>(
                ProducerFenceSql.SqlServerMaxLsn,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (lsn?.MaxLsn is null or { Length: 0 })
        {
            throw new InvalidOperationException(
                "SQL Server CDC max LSN is unavailable for producer fence capture.");
        }

        return CdcDeliveryPosition.ForSqlServerBytes(lastEventId, lsn.MaxLsn);
    }

    /// <summary>回退准备行；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class RollbackPreparationRow
    {
        public int RollbackState { get; init; }

        public Guid? RollbackGeneration { get; init; }
    }

    /// <summary>流上最后一条 Outbox 事件；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class LastOutboxEventRow
    {
        public Guid CutoffEventId { get; init; }

        public DateTimeOffset CutoffOccurredAtUtc { get; init; }
    }

    /// <summary>MySQL MASTER STATUS 位点；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class MySqlMasterStatusRow
    {
        public string? File { get; init; }

        public long? Position { get; init; }
    }

    /// <summary>SQL Server CDC max LSN；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class SqlServerMaxLsnRow
    {
        public byte[]? MaxLsn { get; init; }
    }

    private static class ProducerFenceSql
    {
        public static readonly SqlStatement FindActivePreparation = new(
            "messaging.producer_fence.find_active_preparation",
            """
            SELECT RollbackState, RollbackGeneration
            FROM fn_messaging_stream_ownership
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
              AND RollbackState = 1
              AND RollbackGeneration = @RollbackGeneration
            """,
            SqlDataScope.Global);

        public static readonly SqlStatement FindLastOutboxEventSqlServer = new(
            "messaging.producer_fence.find_last_outbox.sqlserver",
            """
            SELECT TOP 1 Id AS CutoffEventId, OccurredAtUtc AS CutoffOccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC, Id DESC
            """,
            SqlDataScope.Global);

        public static readonly SqlStatement FindLastOutboxEventMySql = new(
            "messaging.producer_fence.find_last_outbox.mysql",
            """
            SELECT Id AS CutoffEventId, OccurredAtUtc AS CutoffOccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType
              AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT 1
            """,
            SqlDataScope.Global);

        public static readonly SqlStatement ShowMasterStatus = new(
            "messaging.producer_fence.mysql_show_master_status",
            "SHOW MASTER STATUS",
            SqlDataScope.Global);

        public static readonly SqlStatement SqlServerMaxLsn = new(
            "messaging.producer_fence.sqlserver_max_lsn",
            "SELECT CAST(sys.fn_cdc_get_max_lsn() AS varbinary(10)) AS MaxLsn",
            SqlDataScope.Global);
    }
}
