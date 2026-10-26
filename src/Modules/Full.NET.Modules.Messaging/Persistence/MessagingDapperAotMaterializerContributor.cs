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
        registrar.Register<ShadowCaptureRow>(reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            AotDataReaderExtensions.ReadInt32(reader, 4), reader.GetString(5), AotDataReaderExtensions.ReadInt32(reader, 6),
            AotDataReaderExtensions.ReadInt64(reader, 7), AotDataReaderExtensions.ReadInt32(reader, 8), AotDataReaderExtensions.ReadInt64(reader, 9),
            AotDataReaderExtensions.ReadInt32(reader, 10), AotDataReaderExtensions.ReadNullableGuid(reader, 11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 12), AotDataReaderExtensions.ReadDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14), AotDataReaderExtensions.ReadNullableString(reader, 15),
            reader.GetGuid(16), reader.GetString(17), reader.GetString(18), AotDataReaderExtensions.ReadDateTimeOffset(reader, 19),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 20), AotDataReaderExtensions.ReadNullableGuid(reader, 21)));
        registrar.Register<Features.GetDeliveryStatus.DeliveryStreamObservationRow>(reader => new(
            AotDataReaderExtensions.ReadNullableString(reader, 0), reader.IsDBNull(1) ? null : AotDataReaderExtensions.ReadInt32(reader, 1),
            AotDataReaderExtensions.ReadInt64(reader, 2), AotDataReaderExtensions.ReadInt64(reader, 3),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 4), AotDataReaderExtensions.ReadInt64(reader, 5),
            AotDataReaderExtensions.ReadInt64(reader, 6), AotDataReaderExtensions.ReadNullableGuid(reader, 7)));
        registrar.Register<KafkaReplayOperationRow>(reader => new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), AotDataReaderExtensions.ReadInt32(reader, 3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetString(8), AotDataReaderExtensions.ReadInt32(reader, 9), AotDataReaderExtensions.ReadNullableGuid(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11), reader.IsDBNull(12) ? null : AotDataReaderExtensions.ReadInt32(reader, 12),
            reader.IsDBNull(13) ? null : reader.GetInt64(13), reader.GetInt64(14), reader.GetInt64(15), reader.GetInt64(16),
            AotDataReaderExtensions.ReadNullableString(reader, 17), reader.GetGuid(18), reader.GetString(19), reader.GetString(20),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 21), AotDataReaderExtensions.ReadDateTimeOffset(reader, 22)));
        registrar.Register<Full.NET.Messaging.Abstractions.EventDeliveryRollbackOperation>(reader => new(
            reader.GetGuid(0), reader.GetString(1), AotDataReaderExtensions.ReadInt32(reader, 2),
            (Full.NET.Messaging.Abstractions.EventDeliveryRollbackPhase)AotDataReaderExtensions.ReadInt32(reader, 3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5), AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            reader.GetBoolean(7), AotDataReaderExtensions.ReadNullableGuid(reader, 8), AotDataReaderExtensions.ReadNullableString(reader, 9)));
        registrar.Register<OutboxStreamCutoffRecord>(ReadOutboxStreamCutoffRecord);
        registrar.Register<Full.NET.Messaging.Abstractions.CutoverShadowEvent>(reader => new(reader.GetGuid(0), reader.GetString(1),
            reader.GetFieldValue<byte[]>(2), AotDataReaderExtensions.ReadDateTimeOffset(reader, 3), reader.GetString(4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5), AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7), AotDataReaderExtensions.ReadNullableString(reader, 8), reader.GetString(9), reader.GetGuid(10)));
        registrar.Register<Features.ChangeDeliveryOwner.CutoverEvidenceRow>(reader => new(reader.GetGuid(0), reader.GetString(1),
            AotDataReaderExtensions.ReadInt32(reader, 2), reader.GetString(3), reader.GetString(4), AotDataReaderExtensions.ReadInt32(reader, 5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6), AotDataReaderExtensions.ReadDateTimeOffset(reader, 7)));
        registrar.Register<DeadLetterRecord>(ReadDeadLetterRecord);
        registrar.Register<OutboxEnvelopeRecord>(ReadOutboxEnvelopeRecord);
        registrar.Register<KafkaDeadLetterRow>(reader => new(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), AotDataReaderExtensions.ReadInt32(reader, 3),
            reader.GetInt64(4), reader.GetString(5), AotDataReaderExtensions.ReadInt32(reader, 6), reader.GetInt64(7),
            AotDataReaderExtensions.ReadNullableGuid(reader, 8), AotDataReaderExtensions.ReadNullableString(reader, 9),
            reader.IsDBNull(10) ? null : AotDataReaderExtensions.ReadInt32(reader, 10),
            AotDataReaderExtensions.ReadNullableGuid(reader, 11), AotDataReaderExtensions.ReadInt32(reader, 12),
            reader.GetString(13), AotDataReaderExtensions.ReadDateTimeOffset(reader, 14),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 15), reader.GetBoolean(16),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 17), reader.GetString(18)));
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
