using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;

namespace Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

internal sealed class DeliveryCutoverService(
    IOutboxBacklogReader backlogReader,
    IntegrationEventSubscriptionCatalog catalog,
    IEffectiveEventDeliveryOwnerResolver ownerResolver,
    EventStreamOwnershipStore ownershipStore,
    IIdGenerator idGenerator,
    IClock clock,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<DeliveryCutoverResponse>> CutoverAsync(
        ChangeDeliveryOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Task.FromResult(Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.ReasonRequired,
                "A cutover reason is required.",
                ErrorType.Validation)));
        }

        if (request.TargetOwner is not EventDeliveryOwner.CdcKafka)
        {
            return Task.FromResult(Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.InvalidCutoverTarget,
                "Only CDC Kafka ownership can be requested through this endpoint.",
                ErrorType.BusinessRule)));
        }

        return transaction.ExecuteResultAsync(
            token => CutoverCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<Result<DeliveryCutoverResponse>> CutoverCoreAsync(
        ChangeDeliveryOwnerRequest request,
        CancellationToken cancellationToken)
    {
        EventDeliveryOwner currentOwner;
        try
        {
            currentOwner = await ownerResolver
                .GetDeliveryOwnerAsync(request.EventType, request.SchemaVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.CutoverPreconditionFailed,
                "The event stream is not registered in the topic catalog.",
                ErrorType.BusinessRule));
        }

        if (currentOwner is not EventDeliveryOwner.LegacyPolling)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.CutoverPreconditionFailed,
                "Cutover is only supported from legacy polling ownership.",
                ErrorType.BusinessRule));
        }

        var retirement = await backlogReader
            .ReadVersionRetirementAsync(
                [request.EventType],
                request.SchemaVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (retirement.PendingCount > 0 || retirement.DeadLetterCount > 0)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.LegacyBacklogNotDrained,
                "Legacy outbox backlog must be drained before cutover.",
                ErrorType.BusinessRule));
        }

        // 精确按目标流检查到期重试、活动租约等瞬时状态；其他事件流的积压不再阻塞本次切流。
        var targetStreamBacklog = await backlogReader
            .ReadStreamBacklogAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        if (targetStreamBacklog.DueRetryCount > 0 || targetStreamBacklog.ActiveLeaseCount > 0)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.LegacyBacklogNotDrained,
                "Target event stream legacy outbox has active leases or due retries; wait for them to complete before cutover.",
                ErrorType.BusinessRule));
        }

        var topic = catalog.GetTopicRequired(request.EventType, request.SchemaVersion);
        var cutoff = await ownershipStore
            .FindLastOutboxEventAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        var cutoffEventId = cutoff?.CutoffEventId ?? Guid.Empty;
        var cutoffOccurredAtUtc = cutoff?.CutoffOccurredAtUtc ?? clock.UtcNow;
        var now = clock.UtcNow;
        var ownershipRecord = new EventStreamOwnershipRecord(
            request.EventType,
            request.SchemaVersion,
            topic.TopicCode,
            request.TargetOwner,
            EventDeliveryOwner.LegacyPolling,
            cutoffEventId,
            cutoffOccurredAtUtc,
            null,
            null,
            request.Reason,
            null,
            null,
            now,
            now);
        try
        {
            await ownershipStore.UpsertAsync(ownershipRecord, cancellationToken).ConfigureAwait(false);
        }
        catch (EventStreamOwnershipConcurrencyException ex)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.CutoverConcurrencyConflict,
                $"Cutover concurrency conflict for '{ex.MessageType}' schema {ex.SchemaVersion}: " +
                $"owner changed from {ex.ExpectedOwner} to {ex.ActualOwner} concurrently. " +
                $"Refresh current owner status and retry.",
                ErrorType.Conflict));
        }

        await domainAuditWriter.WriteAsync(
                new MessagingDomainAuditWrite(
                    MessagingDomainAuditActionKeys.DeliveryCutover,
                    cutoffEventId != Guid.Empty ? cutoffEventId : idGenerator.NewId(),
                    TenantId: null,
                    MessagingDomainAuditOutcomes.Success,
                    ActorUserId: null,
                    ActorDisplayName: null,
                    DiffSummaryJson: JsonSerializer.Serialize(
                        new
                        {
                            eventType = request.EventType,
                            schemaVersion = request.SchemaVersion,
                            currentOwner = currentOwner.ToString(),
                            targetOwner = request.TargetOwner.ToString(),
                            cutoffEventId,
                            cutoffOccurredAtUtc,
                            reason = request.Reason,
                            ownershipPersisted = true,
                        },
                        JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<DeliveryCutoverResponse>.Success(
            new DeliveryCutoverResponse(
                request.EventType,
                request.SchemaVersion,
                currentOwner,
                request.TargetOwner,
                OwnershipPersisted: true,
                cutoffEventId,
                cutoffOccurredAtUtc));
    }
}
