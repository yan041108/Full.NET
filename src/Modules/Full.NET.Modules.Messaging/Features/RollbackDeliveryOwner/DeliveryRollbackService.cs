using System.Text.Json;
using Full.NET.Modules.Messaging.Serialization;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
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
    IEventStreamOwnershipGate ownershipGate,
    IEventDeliveryRollbackReadinessReader rollbackReadinessReader,
    EventStreamOwnershipStore ownershipStore,
    IIdGenerator idGenerator,
    IClock clock,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<DeliveryRollbackResponse>> RollbackAsync(
        ChangeDeliveryOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.ReasonRequired,
                "A rollback reason is required.",
                ErrorType.Validation));
        }

        if (request.TargetOwner is not EventDeliveryOwner.LegacyPolling)
        {
            return Result<DeliveryRollbackResponse>.Failure(new Error(
                MessagingErrorCodes.InvalidRollbackTarget,
                "Only legacy polling ownership can be requested through rollback.",
                ErrorType.BusinessRule));
        }

        var beginResult = await transaction.ExecuteResultAsync(
                token => BeginRollbackAsync(request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!beginResult.IsSuccess)
        {
            return Result<DeliveryRollbackResponse>.Failure(beginResult.Error!);
        }

        var preparation = beginResult.Value!;
        Result<DeliveryRollbackResponse> completion;
        try
        {
            var readiness = await rollbackReadinessReader
                .PrepareAsync(
                    request.EventType,
                    request.SchemaVersion,
                    preparation.RollbackGeneration,
                    cancellationToken)
                .ConfigureAwait(false);
            completion = await transaction.ExecuteResultAsync(
                    token => CompleteRollbackAsync(request, preparation, readiness, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await AbortPreparationAsync(request, preparation.RollbackGeneration)
                .ConfigureAwait(false);
            throw;
        }

        if (completion.IsSuccess)
        {
            return completion;
        }

        await AbortPreparationAsync(request, preparation.RollbackGeneration)
            .ConfigureAwait(false);
        return completion;
    }

    private async Task<Result<RollbackPreparation>> BeginRollbackAsync(
        ChangeDeliveryOwnerRequest request,
        CancellationToken cancellationToken)
    {
        if (!await ownershipGate.AcquireOwnershipChangeAsync(
                request.EventType,
                request.SchemaVersion,
                cancellationToken).ConfigureAwait(false))
        {
            return RollbackPreparationFailure(
                "The event stream has no persisted ownership row and cannot be rolled back safely.");
        }

        EventDeliveryOwner currentOwner;
        try
        {
            currentOwner = await ownerResolver
                .GetDeliveryOwnerAsync(request.EventType, request.SchemaVersion, cancellationToken)
                .ConfigureAwait(false);
            _ = catalog.GetTopicRequired(request.EventType, request.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            return RollbackPreparationFailure(
                "The event stream is not registered in the topic catalog.");
        }

        if (currentOwner is not EventDeliveryOwner.CdcKafka)
        {
            return RollbackPreparationFailure(
                "Rollback is only supported from CDC Kafka ownership.");
        }

        var existing = await ownershipStore
            .FindAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return RollbackPreparationFailure(
                "No persisted ownership record exists for rollback.");
        }

        var rollbackGeneration = idGenerator.NewId();
        var preparedAtUtc = clock.UtcNow;
        if (!await ownershipStore.TryBeginRollbackPreparationAsync(
                request.EventType,
                request.SchemaVersion,
                rollbackGeneration,
                preparedAtUtc,
                cancellationToken).ConfigureAwait(false))
        {
            return RollbackPreparationFailure(
                "Another rollback preparation already owns the producer fence.");
        }

        return Result<RollbackPreparation>.Success(
            new RollbackPreparation(rollbackGeneration, preparedAtUtc));
    }

    private async Task<Result<DeliveryRollbackResponse>> CompleteRollbackAsync(
        ChangeDeliveryOwnerRequest request,
        RollbackPreparation preparation,
        EventDeliveryRollbackReadiness readiness,
        CancellationToken cancellationToken)
    {
        if (!await ownershipGate.AcquireOwnershipChangeAsync(
                request.EventType,
                request.SchemaVersion,
                cancellationToken).ConfigureAwait(false))
        {
            return RollbackFailure(
                "The event stream has no persisted ownership row and cannot be rolled back safely.");
        }

        EventDeliveryOwner currentOwner;
        try
        {
            currentOwner = await ownerResolver
                .GetDeliveryOwnerAsync(request.EventType, request.SchemaVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return RollbackFailure("The event stream is not registered in the topic catalog.");
        }

        if (currentOwner is not EventDeliveryOwner.CdcKafka)
        {
            return RollbackFailure("Rollback is only supported from CDC Kafka ownership.");
        }

        var existing = await ownershipStore
            .FindAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        var persistedPreparation = await ownershipStore
            .FindRollbackPreparationAsync(
                request.EventType,
                request.SchemaVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null
            || persistedPreparation is null
            || persistedPreparation.RollbackState != 1
            || persistedPreparation.RollbackGeneration != preparation.RollbackGeneration)
        {
            return RollbackFailure(
                "The persisted rollback producer fence is missing or belongs to another generation.");
        }

        var rollbackOccurredAtUtc = clock.UtcNow;
        var readinessAge = rollbackOccurredAtUtc - readiness.ObservedAtUtc;
        if (readiness.RollbackGeneration != preparation.RollbackGeneration
            || !readiness.ConnectorStopped
            || !readiness.BrokerMessagesDrainedOrIsolated
            || !readiness.SourcePositionCoversProducerFence
            || string.IsNullOrWhiteSpace(readiness.ProducerFencePositionJson)
            || string.IsNullOrWhiteSpace(readiness.CdcSourcePositionJson)
            || string.IsNullOrWhiteSpace(readiness.ControlPlaneFenceToken)
            || readinessAge < TimeSpan.Zero
            || readinessAge > TimeSpan.FromMinutes(1))
        {
            return RollbackFailure(
                "Rollback requires fresh control-plane proof that the Connector is fenced, Broker messages are drained or isolated, and the CDC source position covers the persisted producer fence.");
        }

        var topic = catalog.GetTopicRequired(request.EventType, request.SchemaVersion);
        var rollbackBoundaryEventId = readiness.LastPublishedEventId ?? existing.CutoffEventId;
        var ownershipRecord = existing with
        {
            TopicCode = topic.TopicCode,
            CurrentOwner = EventDeliveryOwner.LegacyPolling,
            PreviousOwner = EventDeliveryOwner.CdcKafka,
            CdcSourcePositionJson = readiness.CdcSourcePositionJson,
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
                        new DeliveryRollbackAuditDiff(
                            request.EventType,
                            request.SchemaVersion,
                            currentOwner.ToString(),
                            request.TargetOwner.ToString(),
                            preparation.RollbackGeneration,
                            readiness.ProducerFencePositionJson,
                            rollbackBoundaryEventId,
                            rollbackOccurredAtUtc,
                            request.Reason,
                            OwnershipPersisted: true),
                        MessagingJsonSerializerContext.Default.DeliveryRollbackAuditDiff)),
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

    private async Task AbortPreparationAsync(
        ChangeDeliveryOwnerRequest request,
        Guid rollbackGeneration)
    {
        // 必须先确认同一 generation 的 Connector/Consumer 已恢复，再解除数据库
        // producer fence；否则控制面恢复失败会让新 Outbox 行进入已停止的 CDC 链路。
        await rollbackReadinessReader.AbortAsync(
                request.EventType,
                request.SchemaVersion,
                rollbackGeneration,
                CancellationToken.None)
            .ConfigureAwait(false);
        await transaction.ExecuteAsync(
                async token =>
                {
                    if (await ownershipGate.AcquireOwnershipChangeAsync(
                            request.EventType,
                            request.SchemaVersion,
                            token).ConfigureAwait(false))
                    {
                        await ownershipStore.TryAbortRollbackPreparationAsync(
                                request.EventType,
                                request.SchemaVersion,
                                rollbackGeneration,
                                clock.UtcNow,
                                token)
                            .ConfigureAwait(false);
                    }

                    return 0;
                },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static Result<RollbackPreparation> RollbackPreparationFailure(string message) =>
        Result<RollbackPreparation>.Failure(new Error(
            MessagingErrorCodes.RollbackPreconditionFailed,
            message,
            ErrorType.BusinessRule));

    private static Result<DeliveryRollbackResponse> RollbackFailure(string message) =>
        Result<DeliveryRollbackResponse>.Failure(new Error(
            MessagingErrorCodes.RollbackPreconditionFailed,
            message,
            ErrorType.BusinessRule));

    private sealed record RollbackPreparation(
        Guid RollbackGeneration,
        DateTimeOffset PreparedAtUtc);
}
