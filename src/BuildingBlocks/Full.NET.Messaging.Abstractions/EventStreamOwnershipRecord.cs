namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 事件流交付所有权的持久化记录；用于切流、回退边界与有效所有权解析。
/// </summary>
public sealed record EventStreamOwnershipRecord(
    string MessageType,
    int SchemaVersion,
    string TopicCode,
    EventDeliveryOwner CurrentOwner,
    EventDeliveryOwner PreviousOwner,
    Guid CutoffEventId,
    DateTimeOffset CutoffOccurredAtUtc,
    string? CdcSourcePositionJson,
    Guid? OperatorUserId,
    string Reason,
    Guid? RollbackBoundaryEventId,
    DateTimeOffset? RollbackOccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
