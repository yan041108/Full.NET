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

namespace Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;

internal sealed class DeliveryRollbackService(
    IntegrationEventSubscriptionCatalog catalog,
    IEffectiveEventDeliveryOwnerResolver ownerResolver,
    EventStreamOwnershipStore ownershipStore,
    IIdGenerator idGenerator,
    IClock clock,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<DeliveryRollbackResponse>> RollbackAsync(
        ChangeDeliveryOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Task.FromResult(Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.ReasonRequired,
                "A rollback reason is required.",
                ErrorType.Validation)));
        }

        if (request.TargetOwner is not EventDeliveryOwner.LegacyPolling)
        {
            return Task.FromResult(Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.InvalidRollbackTarget,
                "Only legacy polling ownership can be requested through rollback.",
                ErrorType.BusinessRule)));
        }

        return transaction.ExecuteResultAsync(
            token => RollbackCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<Result<DeliveryRollbackResponse>> RollbackCoreAsync(
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
            return Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.RollbackPreconditionFailed,
                "The event stream is not registered in the topic catalog.",
                ErrorType.BusinessRule));
        }

        if (currentOwner is not EventDeliveryOwner.CdcKafka)
        {
            return Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.RollbackPreconditionFailed,
                "Rollback is only supported from CDC Kafka ownership.",
                ErrorType.BusinessRule));
        }

        var existing = await ownershipStore
            .FindAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.RollbackPreconditionFailed,
                "No persisted ownership record exists for rollback.",
                ErrorType.BusinessRule));
        }

        var topic = catalog.GetTopicRequired(request.EventType, request.SchemaVersion);
        var rollbackBoundary = await ownershipStore
            .FindLastOutboxEventAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        var rollbackBoundaryEventId = rollbackBoundary?.CutoffEventId ?? existing.CutoffEventId;
        var rollbackOccurredAtUtc = clock.UtcNow;
        var ownershipRecord = existing with
        {
            TopicCode = topic.TopicCode,
            CurrentOwner = EventDeliveryOwner.LegacyPolling,
            PreviousOwner = EventDeliveryOwner.CdcKafka,
            Reason = request.Reason,
            RollbackBoundaryEventId = rollbackBoundaryEventId,
            RollbackOccurredAtUtc = rollbackOccurredAtUtc,
            UpdatedAtUtc = rollbackOccurredAtUtc,
        };
        await ownershipStore.UpsertAsync(ownershipRecord, cancellationToken).ConfigureAwait(false);

        await domainAuditWriter.WriteAsync(
                new MessagingDomainAuditWrite(
                    MessagingDomainAuditActionKeys.DeliveryRollback,
                    rollbackBoundaryEventId != Guid.Empty
                        ? rollbackBoundaryEventId
                        : idGenerator.NewId(),
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
                            rollbackBoundaryEventId,
                            rollbackOccurredAtUtc,
                            reason = request.Reason,
                            ownershipPersisted = true,
                        },
                        JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<DeliveryRollbackResponse>.Success(
            new DeliveryRollbackResponse(
                request.EventType,
                request.SchemaVersion,
                currentOwner,
                request.TargetOwner,
                OwnershipPersisted: true,
                rollbackBoundaryEventId,
                rollbackOccurredAtUtc));
    }
}
