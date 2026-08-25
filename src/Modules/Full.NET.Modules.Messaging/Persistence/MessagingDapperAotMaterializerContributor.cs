#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>
/// Messaging 模块 Native AOT 行物化器注册，覆盖事件流所有权与切流边界查询投影。
/// </summary>
internal sealed class MessagingDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<EventStreamOwnershipPersistenceRow>(ReadEventStreamOwnershipPersistenceRow);
        registrar.Register<RollbackPreparationRecord>(ReadRollbackPreparationRecord);
        registrar.Register<OutboxStreamCutoffRecord>(ReadOutboxStreamCutoffRecord);
    }

    private static EventStreamOwnershipPersistenceRow ReadEventStreamOwnershipPersistenceRow(
        DbDataReader reader) =>
        new()
        {
            MessageType = reader.GetString(0),
            SchemaVersion = reader.GetInt32(1),
            TopicCode = reader.GetString(2),
            CurrentOwner = reader.GetInt32(3),
            PreviousOwner = reader.GetInt32(4),
            CutoffEventId = reader.GetGuid(5),
            CutoffOccurredAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            CdcSourcePositionJson = AotDataReaderExtensions.ReadNullableString(reader, 7),
            OperatorUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            Reason = reader.GetString(9),
            RollbackBoundaryEventId = AotDataReaderExtensions.ReadNullableGuid(reader, 10),
            RollbackOccurredAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            RollbackState = reader.GetInt32(12),
            RollbackGeneration = AotDataReaderExtensions.ReadNullableGuid(reader, 13),
            RollbackPreparedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
            UpdatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 16),
        };

    private static RollbackPreparationRecord ReadRollbackPreparationRecord(DbDataReader reader) =>
        new()
        {
            RollbackState = reader.GetInt32(0),
            RollbackGeneration = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            RollbackPreparedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 2),
        };

    private static OutboxStreamCutoffRecord ReadOutboxStreamCutoffRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 1));
}
#endif
