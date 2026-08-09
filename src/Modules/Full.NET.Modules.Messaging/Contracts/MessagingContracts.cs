using Full.NET.Abstractions.Results;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Contracts;

public static class MessagingPermissions
{
    public const string EventsRead = "messaging.events.read";

    public const string DeadLettersRead = "messaging.dead_letters.read";

    public const string DeadLettersReplay = "messaging.dead_letters.replay";

    public const string DeliveryCutover = "messaging.delivery.cutover";

    public const string DeliveryRollback = "messaging.delivery.rollback";
}

public static class MessagingErrorCodes
{
    public const string Prefix = "messaging.";

    public const string DeadLetterNotFound = "messaging.dead_letter.not_found";

    public const string OutboxEventNotFound = "messaging.outbox_event.not_found";

    public const string SubscriptionRouteNotFound = "messaging.subscription_route.not_found";

    public const string LegacyBacklogNotDrained = "messaging.delivery.legacy_backlog_not_drained";

    public const string CutoverPreconditionFailed = "messaging.delivery.cutover_precondition_failed";

    public const string InvalidCutoverTarget = "messaging.delivery.invalid_cutover_target";

    public const string InvalidRollbackTarget = "messaging.delivery.invalid_rollback_target";

    public const string RollbackPreconditionFailed = "messaging.delivery.rollback_precondition_failed";

    public const string ReasonRequired = "messaging.delivery.reason_required";
}

public static class DeadLetterReplayOutcomes
{
    public const string Processed = "processed";

    public const string AlreadyProcessed = "already_processed";
}

public sealed record DeadLetterResponse(
    string ConsumerName,
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    Guid? TenantId,
    int Attempts,
    DateTimeOffset ReceivedAtUtc,
    string? LastErrorCode,
    string? LastError);

public sealed record ReplayDeadLetterRequest(string ConsumerName, Guid MessageId);

public sealed record DeadLetterReplayResponse(
    Guid MessageId,
    string ConsumerName,
    string Outcome);

public sealed record OutboxBacklogSummaryResponse(
    long PendingCount,
    long DueRetryCount,
    long ActiveLeaseCount,
    long DeadLetterCount,
    DateTimeOffset? OldestOccurredAtUtc,
    DateTimeOffset? OldestDeadLetteredAtUtc);

public sealed record EventStreamStatusResponse(
    string EventType,
    int SchemaVersion,
    string TopicCode,
    EventDeliveryOwner DeliveryOwner);

public sealed record DeliveryStatusResponse(
    OutboxBacklogSummaryResponse Backlog,
    IReadOnlyList<EventStreamStatusResponse> Streams);

public sealed record ChangeDeliveryOwnerRequest(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner TargetOwner,
    string Reason);

public sealed record DeliveryCutoverResponse(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner CurrentOwner,
    EventDeliveryOwner TargetOwner,
    bool OwnershipPersisted,
    Guid CutoffEventId,
    DateTimeOffset CutoffOccurredAtUtc);

public sealed record DeliveryRollbackResponse(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner CurrentOwner,
    EventDeliveryOwner TargetOwner,
    bool OwnershipPersisted,
    Guid RollbackBoundaryEventId,
    DateTimeOffset RollbackOccurredAtUtc);
