using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

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
        new(
            record.MessageType,
            record.SchemaVersion,
            record.TopicCode,
            (sbyte)record.CurrentOwner,
            (sbyte)record.PreviousOwner,
            record.CutoffEventId,
            record.CutoffOccurredAtUtc,
            record.CdcSourcePositionJson,
            record.OperatorUserId,
            record.Reason,
            record.RollbackBoundaryEventId,
            record.RollbackOccurredAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);
}
