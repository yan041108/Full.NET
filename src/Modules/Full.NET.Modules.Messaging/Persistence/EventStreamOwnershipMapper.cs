using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>
/// 在 <see cref="EventStreamOwnershipRecord"/> 领域记录与持久化行之间互转，
/// 所有者字段在 <see cref="EventDeliveryOwner"/> 枚举与 int 存储值之间强制转换。
/// </summary>
/// <remarks>
/// <c>ToPersistenceRow</c> 固定将回退准备态字段重置为初始值，回退状态由专用 SQL 语句维护而非通用 Upsert。
/// </remarks>
internal static class EventStreamOwnershipMapper
{
    public static EventStreamOwnershipRecord ToRecord(EventStreamOwnershipPersistenceRow row) =>
        new(
            row.MessageType,
            row.SchemaVersion,
            row.TopicCode,
            (EventDeliveryOwner)row.CurrentOwner,
            (EventDeliveryOwner)row.PreviousOwner,
            row.CutoffEventId,
            row.CutoffOccurredAtUtc,
            row.CdcSourcePositionJson,
            row.OperatorUserId,
            row.Reason,
            row.RollbackBoundaryEventId,
            row.RollbackOccurredAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    public static EventStreamOwnershipPersistenceRow ToPersistenceRow(
        EventStreamOwnershipRecord record) =>
        new()
        {
            MessageType = record.MessageType,
            SchemaVersion = record.SchemaVersion,
            TopicCode = record.TopicCode,
            CurrentOwner = (int)record.CurrentOwner,
            PreviousOwner = (int)record.PreviousOwner,
            CutoffEventId = record.CutoffEventId,
            CutoffOccurredAtUtc = record.CutoffOccurredAtUtc,
            CdcSourcePositionJson = record.CdcSourcePositionJson,
            OperatorUserId = record.OperatorUserId,
            Reason = record.Reason,
            RollbackBoundaryEventId = record.RollbackBoundaryEventId,
            RollbackOccurredAtUtc = record.RollbackOccurredAtUtc,
            RollbackState = 0,
            RollbackGeneration = null,
            RollbackPreparedAtUtc = null,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
        };
}
