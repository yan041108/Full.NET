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
