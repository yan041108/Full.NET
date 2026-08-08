using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

internal sealed class DeliveryCutoverService(
    IOutboxBacklogReader backlogReader,
    IntegrationEventSubscriptionCatalog catalog,
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
            currentOwner = catalog.GetDeliveryOwner(request.EventType, request.SchemaVersion);
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

        var backlog = await backlogReader.ReadBacklogAsync(cancellationToken).ConfigureAwait(false);
        if (backlog.PendingCount > 0 || backlog.DueRetryCount > 0 || backlog.ActiveLeaseCount > 0)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.LegacyBacklogNotDrained,
                "Legacy outbox backlog must be drained before cutover.",
                ErrorType.BusinessRule));
        }

        await domainAuditWriter.WriteAsync(
                new MessagingDomainAuditWrite(
                    MessagingDomainAuditActionKeys.DeliveryCutover,
                    Guid.Empty,
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
                            reason = request.Reason,
                            ownershipPersisted = false,
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
                OwnershipPersisted: false));
    }
}
