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
        registrar.Register<DeadLetterRecord>(ReadDeadLetterRecord);
        registrar.Register<OutboxEnvelopeRecord>(ReadOutboxEnvelopeRecord);
    }

    /// <remarks>
    /// SQL Server 上 CurrentOwner、PreviousOwner、RollbackState 为 tinyint，
    /// <see cref="DbDataReader.GetInt32"/> 会抛出 InvalidCastException；
    /// SchemaVersion 虽为 int，仍统一走 Convert 以免双库驱动返回类型漂移。
    /// </remarks>
    private static EventStreamOwnershipPersistenceRow ReadEventStreamOwnershipPersistenceRow(
        DbDataReader reader) =>
        new()
        {
            MessageType = reader.GetString(0),
            SchemaVersion = AotDataReaderExtensions.ReadInt32(reader, 1),
            TopicCode = reader.GetString(2),
            CurrentOwner = AotDataReaderExtensions.ReadInt32(reader, 3),
            PreviousOwner = AotDataReaderExtensions.ReadInt32(reader, 4),
            CutoffEventId = reader.GetGuid(5),
            CutoffOccurredAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            CdcSourcePositionJson = AotDataReaderExtensions.ReadNullableString(reader, 7),
            OperatorUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            Reason = reader.GetString(9),
            RollbackBoundaryEventId = AotDataReaderExtensions.ReadNullableGuid(reader, 10),
            RollbackOccurredAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            RollbackState = AotDataReaderExtensions.ReadInt32(reader, 12),
            RollbackGeneration = AotDataReaderExtensions.ReadNullableGuid(reader, 13),
            RollbackPreparedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
            UpdatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 16),
        };

    private static RollbackPreparationRecord ReadRollbackPreparationRecord(DbDataReader reader) =>
        new()
        {
            RollbackState = AotDataReaderExtensions.ReadInt32(reader, 0),
            RollbackGeneration = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            RollbackPreparedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 2),
        };

    private static OutboxStreamCutoffRecord ReadOutboxStreamCutoffRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 1));

    private static DeadLetterRecord ReadDeadLetterRecord(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt32(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4),
            reader.GetInt32(5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            AotDataReaderExtensions.ReadNullableString(reader, 8));

    private static OutboxEnvelopeRecord ReadOutboxEnvelopeRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            reader.GetString(9),
            reader.GetFieldValue<byte[]>(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11));
}
#endif
