#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper.Inbox;
using Full.NET.Data.Dapper.Outbox;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper 基础设施自身的 Native AOT 行物化注册，避免依赖业务模块启动顺序。
/// </summary>
internal static class DapperAotInfrastructureRegistration
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (RegistrationLock)
        {
            if (_registered)
            {
                return;
            }

            DapperAotMaterializerRegistry.Register<InboxClaimRow>(reader =>
                new InboxClaimRow(
                    reader.GetString(0),
                    reader.GetFieldValue<byte[]>(1)));
            DapperAotMaterializerRegistry.Register<InboxBatchPrecheckRow>(reader =>
                new InboxBatchPrecheckRow(
                    reader.GetInt32(0),
                    AotDataReaderExtensions.ReadNullableString(reader, 1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2)));
            DapperAotMaterializerRegistry.Register<OutboxStreamCutoffSnapshot>(
                ReadOutboxStreamCutoffSnapshot);
            DapperAotMaterializerRegistry.Register<DapperOutboxStore.OutboxRow>(ReadOutboxRow);
            DapperAotMaterializerRegistry.Register<DapperOutboxStore.SqlServerBacklogRow>(
                ReadSqlServerBacklogRow);
            DapperAotMaterializerRegistry.Register<DapperOutboxStore.MySqlBacklogRow>(
                ReadMySqlBacklogRow);
            DapperAotMaterializerRegistry.Register<DapperOutboxStore.SqlServerVersionRetirementRow>(
                ReadSqlServerVersionRetirementRow);
            DapperAotMaterializerRegistry.Register<DapperOutboxStore.MySqlVersionRetirementRow>(
                ReadMySqlVersionRetirementRow);
            DapperAotMaterializerRegistry.Register<DapperOutboxStore.MySqlOutboxRow>(
                ReadMySqlOutboxRow);
            DapperAotMaterializerRegistry.Register<DapperEventDeliveryProducerFencePositionReader.RollbackPreparationRow>(
                ReadRollbackPreparationRow);
            DapperAotMaterializerRegistry.Register<DapperEventDeliveryProducerFencePositionReader.LastOutboxEventRow>(
                ReadLastOutboxEventRow);
            DapperAotMaterializerRegistry.Register<DapperEventDeliveryProducerFencePositionReader.MySqlMasterStatusRow>(
                ReadMySqlMasterStatusRow);
            DapperAotMaterializerRegistry.Register<DapperEventDeliveryProducerFencePositionReader.SqlServerMaxLsnRow>(
                ReadSqlServerMaxLsnRow);

            DapperAotParameterRegistry.Register<OutboxMessage>(BindOutboxMessage);
            DapperAotParameterRegistry.Register<AppendOnlyOutboxMessage>(
                BindAppendOnlyOutboxMessage);
            DapperAotParameterRegistry.Register<DapperOutboxStore.OutboxAcquireParameters>(
                BindOutboxAcquireParameters);
            DapperAotStaticCommandPlanRegistry.Register(
                "outbox.insert",
                [
                    "Id",
                    "MessageType",
                    "SchemaVersion",
                    "ContentType",
                    "TenantId",
                    "TraceId",
                    "Payload",
                    "OccurredAtUtc",
                ]);
            DapperAotStaticCommandPlanRegistry.Register(
                "messaging.outbox.append",
                [
                    "Id",
                    "MessageType",
                    "SchemaVersion",
                    "ContentType",
                    "TenantId",
                    "PartitionKey",
                    "CorrelationId",
                    "CausationId",
                    "TraceParent",
                    "Producer",
                    "Payload",
                    "OccurredAtUtc",
                ]);
            _registered = true;
        }
    }

    private static OutboxStreamCutoffSnapshot ReadOutboxStreamCutoffSnapshot(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 1));

    private static DapperOutboxStore.OutboxRow ReadOutboxRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            LockId = reader.GetGuid(1),
            MessageType = reader.GetString(2),
            SchemaVersion = AotDataReaderExtensions.ReadInt32(reader, 3),
            ContentType = reader.GetString(4),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            TraceId = AotDataReaderExtensions.ReadNullableString(reader, 6),
            Payload = reader.GetFieldValue<byte[]>(7),
            Attempts = AotDataReaderExtensions.ReadInt32(reader, 8),
            OccurredAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
        };

    private static DapperOutboxStore.SqlServerBacklogRow ReadSqlServerBacklogRow(
        DbDataReader reader) =>
        new()
        {
            PendingCount = AotDataReaderExtensions.ReadInt64(reader, 0),
            OldestOccurredAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 1),
            DueRetryCount = AotDataReaderExtensions.ReadInt64(reader, 2),
            ActiveLeaseCount = AotDataReaderExtensions.ReadInt64(reader, 3),
            DeadLetterCount = AotDataReaderExtensions.ReadInt64(reader, 4),
            OldestDeadLetteredAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 5),
        };

    private static DapperOutboxStore.MySqlBacklogRow ReadMySqlBacklogRow(DbDataReader reader) =>
        new()
        {
            PendingCount = AotDataReaderExtensions.ReadInt64(reader, 0),
            OldestOccurredAtUtc = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            DueRetryCount = AotDataReaderExtensions.ReadInt64(reader, 2),
            ActiveLeaseCount = AotDataReaderExtensions.ReadInt64(reader, 3),
            DeadLetterCount = AotDataReaderExtensions.ReadInt64(reader, 4),
            OldestDeadLetteredAtUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
        };

    private static DapperOutboxStore.SqlServerVersionRetirementRow
        ReadSqlServerVersionRetirementRow(DbDataReader reader) =>
        new()
        {
            PendingCount = AotDataReaderExtensions.ReadInt64(reader, 0),
            DeadLetterCount = AotDataReaderExtensions.ReadInt64(reader, 1),
            OldestUnprocessedOccurredAtUtc =
                AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 2),
        };

    private static DapperOutboxStore.MySqlVersionRetirementRow ReadMySqlVersionRetirementRow(
        DbDataReader reader) =>
        new()
        {
            PendingCount = AotDataReaderExtensions.ReadInt64(reader, 0),
            DeadLetterCount = AotDataReaderExtensions.ReadInt64(reader, 1),
            OldestUnprocessedOccurredAtUtc = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
        };

    private static DapperOutboxStore.MySqlOutboxRow ReadMySqlOutboxRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            LockId = reader.GetGuid(1),
            MessageType = reader.GetString(2),
            SchemaVersion = AotDataReaderExtensions.ReadInt32(reader, 3),
            ContentType = reader.GetString(4),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            TraceId = AotDataReaderExtensions.ReadNullableString(reader, 6),
            Payload = reader.GetFieldValue<byte[]>(7),
            Attempts = AotDataReaderExtensions.ReadInt32(reader, 8),
            OccurredAtUtc = reader.GetDateTime(9),
        };

    private static DapperEventDeliveryProducerFencePositionReader.RollbackPreparationRow
        ReadRollbackPreparationRow(DbDataReader reader) =>
        new()
        {
            RollbackState = AotDataReaderExtensions.ReadInt32(reader, 0),
            RollbackGeneration = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        };

    private static DapperEventDeliveryProducerFencePositionReader.LastOutboxEventRow
        ReadLastOutboxEventRow(DbDataReader reader) =>
        new()
        {
            CutoffEventId = reader.GetGuid(0),
            CutoffOccurredAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 1),
        };

    private static DapperEventDeliveryProducerFencePositionReader.MySqlMasterStatusRow
        ReadMySqlMasterStatusRow(DbDataReader reader) =>
        new()
        {
            File = AotDataReaderExtensions.ReadNullableString(reader, 0),
            Position = reader.IsDBNull(1)
                ? null
                : AotDataReaderExtensions.ReadInt64(reader, 1),
        };

    private static DapperEventDeliveryProducerFencePositionReader.SqlServerMaxLsnRow
        ReadSqlServerMaxLsnRow(DbDataReader reader) =>
        new()
        {
            MaxLsn = reader.IsDBNull(0) ? null : reader.GetFieldValue<byte[]>(0),
        };

    private static DynamicParameters BindOutboxMessage(OutboxMessage message)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", message.Id);
        parameters.Add("MessageType", message.MessageType);
        parameters.Add("SchemaVersion", message.SchemaVersion);
        parameters.Add("ContentType", message.ContentType);
        parameters.Add("TenantId", message.TenantId);
        parameters.Add("TraceId", message.TraceId);
        parameters.Add("Payload", message.Payload);
        parameters.Add("OccurredAtUtc", message.OccurredAtUtc);
        return parameters;
    }

    private static DynamicParameters BindAppendOnlyOutboxMessage(
        AppendOnlyOutboxMessage message)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", message.Id);
        parameters.Add("MessageType", message.MessageType);
        parameters.Add("SchemaVersion", message.SchemaVersion);
        parameters.Add("ContentType", message.ContentType);
        parameters.Add("TenantId", message.TenantId);
        parameters.Add("PartitionKey", message.PartitionKey);
        parameters.Add("CorrelationId", message.CorrelationId);
        parameters.Add("CausationId", message.CausationId);
        parameters.Add("TraceParent", message.TraceParent);
        parameters.Add("Producer", message.Producer);
        parameters.Add("Payload", message.Payload);
        parameters.Add("OccurredAtUtc", message.OccurredAtUtc);
        return parameters;
    }

    private static DynamicParameters BindOutboxAcquireParameters(
        DapperOutboxStore.OutboxAcquireParameters parameters)
    {
        var bound = new DynamicParameters();
        bound.Add("BatchSize", parameters.BatchSize);
        bound.Add("LockId", parameters.LockId);
        bound.Add("Now", parameters.Now);
        bound.Add("LockedUntil", parameters.LockedUntil);
        return bound;
    }
}
#endif
