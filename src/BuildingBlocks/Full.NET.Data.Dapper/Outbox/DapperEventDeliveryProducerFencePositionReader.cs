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
    public async Task<bool> IsSameTransactionCoveredAsync(string eventType, int schemaVersion, Guid rollbackGeneration,
        CdcDeliveryPosition producerFence, CdcDeliveryPosition connector, CancellationToken cancellationToken = default)
    {
        // 提交 LSN 相等不足以证明整个事务已消费；只接受能唯一映射到 CDC 原始行的已确认位置。
        if (databaseOptions.Value.Provider != DatabaseProvider.SqlServer || producerFence.Provider != CdcDeliveryPosition.SqlServerProvider
            || connector.Provider != CdcDeliveryPosition.SqlServerProvider || producerFence.Lsn is null || connector.Lsn is null
            || !producerFence.Lsn.IsValid() || !connector.Lsn.IsValid() || connector.Lsn.EventSerialNo != 1
            || !string.Equals(producerFence.Lsn.CommitLsn, connector.Lsn.CommitLsn, StringComparison.OrdinalIgnoreCase)
            || connector.Lsn.ChangeLsn is not { } change || !new SqlServerCdcLsnCoordinates(change).IsValid()) return false;
        var commitBytes = Convert.FromHexString(producerFence.Lsn.CommitLsn.Replace(":", "", StringComparison.Ordinal));
        var changeBytes = Convert.FromHexString(change.Replace(":", "", StringComparison.Ordinal));
        return await queryExecutor.QuerySingleOrDefaultAsync<int>(ProducerFenceSql.SqlServerSameTransactionCovered,
            DapperSqlParameters.Create(("MessageType", eventType), ("SchemaVersion", schemaVersion),
                ("RollbackGeneration", rollbackGeneration), ("CommitLsn", commitBytes), ("ChangeLsn", changeBytes)), cancellationToken)
            .ConfigureAwait(false) == 1;
    }

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
                    eventType, schemaVersion,
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

        if (position is null) return null;
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

    private async Task<CdcDeliveryPosition?> ReadSqlServerPositionAsync(
        string eventType, int schemaVersion,
        Guid? lastEventId,
        CancellationToken cancellationToken)
    {
        var lsn = await queryExecutor
            .QuerySingleOrDefaultAsync<SqlServerMaxLsnRow>(
                lastEventId is null ? ProducerFenceSql.SqlServerMaxLsn : ProducerFenceSql.SqlServerCapturedStreamLsn,
                DapperSqlParameters.Create(("MessageType", eventType), ("SchemaVersion", schemaVersion)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (lsn?.MaxLsn is null or { Length: 0 })
        {
            // Capture Job 尚未覆盖全部已冻结源行时，不得用旧的全库 max LSN 放行暂停。
            return null;
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
        // SQL Server 按 command_id 排序；Debezium 的函数读取模式还按 seqval 排序。
        // 两种次序都不得存在后续目标行，同一命令的其他目标行按未知顺序保守拒绝。
        public static readonly SqlStatement SqlServerSameTransactionCovered = new(
            "messaging.producer_fence.same_transaction_covered.sql_server",
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM fn_messaging_stream_ownership
                WHERE MessageType=@MessageType AND SchemaVersion=@SchemaVersion
                  AND RollbackState=1 AND RollbackGeneration=@RollbackGeneration
            ) AND (SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT
                   WHERE __$start_lsn=@CommitLsn AND __$seqval=@ChangeLsn)=1
              AND EXISTS (
                SELECT 1 FROM cdc.fullnet_fn_messaging_outbox_event_CT anchor
                INNER JOIN fn_messaging_outbox_event source ON source.Id=anchor.Id
                WHERE anchor.__$start_lsn=@CommitLsn AND anchor.__$seqval=@ChangeLsn AND anchor.__$operation=2
                  AND anchor.__$command_id IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM cdc.fullnet_fn_messaging_outbox_event_CT later
                    INNER JOIN fn_messaging_outbox_event other ON other.Id=later.Id
                    WHERE later.__$start_lsn=@CommitLsn AND later.__$operation=2
                      AND other.MessageType=@MessageType AND other.SchemaVersion=@SchemaVersion
                      AND (later.__$command_id IS NULL OR later.__$seqval IS NULL OR later.__$command_id>anchor.__$command_id OR later.__$seqval>anchor.__$seqval OR
                          (later.__$command_id=anchor.__$command_id AND later.Id<>anchor.Id))
                  )
              )
              AND (SELECT COUNT(DISTINCT changes.Id) FROM cdc.fullnet_fn_messaging_outbox_event_CT changes
                   INNER JOIN fn_messaging_outbox_event source ON source.Id=changes.Id
                   WHERE source.MessageType=@MessageType AND source.SchemaVersion=@SchemaVersion AND changes.__$operation=2)
                  = (SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@MessageType AND SchemaVersion=@SchemaVersion)
              AND (SELECT MAX(changes.__$start_lsn) FROM cdc.fullnet_fn_messaging_outbox_event_CT changes
                   INNER JOIN fn_messaging_outbox_event source ON source.Id=changes.Id
                   WHERE source.MessageType=@MessageType AND source.SchemaVersion=@SchemaVersion AND changes.__$operation=2)=@CommitLsn
              THEN 1 ELSE 0 END
            """, SqlDataScope.Global);

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

        public static readonly SqlStatement SqlServerCapturedStreamLsn = new(
            "messaging.producer_fence.captured_stream_lsn.sql_server",
            """
            SELECT CAST(MAX(changes.__$start_lsn) AS varbinary(10)) AS MaxLsn
            FROM cdc.fullnet_fn_messaging_outbox_event_CT changes
            INNER JOIN fn_messaging_outbox_event source ON source.Id = changes.Id
            WHERE source.MessageType = @MessageType AND source.SchemaVersion = @SchemaVersion
              AND changes.__$operation = 2
            HAVING COUNT(DISTINCT changes.Id) = (
                SELECT COUNT(*) FROM fn_messaging_outbox_event
                WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion)
            """, SqlDataScope.Global);
    }
}
