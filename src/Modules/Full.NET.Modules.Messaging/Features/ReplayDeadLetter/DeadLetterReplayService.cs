using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;

namespace Full.NET.Modules.Messaging.Features.ReplayDeadLetter;

internal sealed class DeadLetterReplayService(
    IQueryExecutor queryExecutor,
    ICommandTransaction transaction,
    IntegrationEventConsumerDispatcher dispatcher,
    IntegrationEventSubscriptionCatalog catalog,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<DeadLetterReplayResponse>> ReplayAsync(
        ReplayDeadLetterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MessageId == Guid.Empty)
        {
            return Task.FromResult(Result<DeadLetterReplayResponse>.Failure(new Error(
                MessagingErrorCodes.DeadLetterNotFound,
                "The dead letter message id is invalid.",
                ErrorType.Validation)));
        }

        return transaction.ExecuteResultAsync(
            token => ReplayCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<Result<DeadLetterReplayResponse>> ReplayCoreAsync(
        ReplayDeadLetterRequest request,
        CancellationToken cancellationToken)
    {
        var deadLetter = await queryExecutor.QuerySingleOrDefaultAsync<DeadLetterRecord>(
                MessagingOperationsSql.FindDeadLetterByKey,
                new
                {
                    ConsumerName = request.ConsumerName,
                    MessageId = request.MessageId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (deadLetter is null)
        {
            return Result<DeadLetterReplayResponse>.Failure(new Error(
                MessagingErrorCodes.DeadLetterNotFound,
                "The dead letter was not found.",
                ErrorType.NotFound));
        }

        var outbox = await queryExecutor.QuerySingleOrDefaultAsync<OutboxEnvelopeRecord>(
                MessagingOperationsSql.FindOutboxEnvelopeById,
                new { Id = request.MessageId },
                cancellationToken)
            .ConfigureAwait(false);
        if (outbox is null)
        {
            return Result<DeadLetterReplayResponse>.Failure(new Error(
                MessagingErrorCodes.OutboxEventNotFound,
                "The outbox envelope for replay was not found.",
                ErrorType.NotFound));
        }

        IIntegrationEventSubscription subscription;
        try
        {
            subscription = catalog.GetRequired(
                request.ConsumerName,
                outbox.MessageType,
                outbox.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            return Result<DeadLetterReplayResponse>.Failure(new Error(
                MessagingErrorCodes.SubscriptionRouteNotFound,
                "The consumer route is not registered in the subscription catalog.",
                ErrorType.BusinessRule));
        }

        var envelope = IntegrationEventEnvelope.Create(
            outbox.Id,
            outbox.MessageType,
            outbox.SchemaVersion,
            outbox.ContentType,
            outbox.TenantId,
            outbox.PartitionKey,
            outbox.CorrelationId,
            outbox.CausationId,
            outbox.TraceParent,
            outbox.Producer,
            outbox.OccurredAtUtc,
            outbox.Payload);

        var consumeResult = await dispatcher
            .ConsumeAsync(request.ConsumerName, envelope, subscription, cancellationToken)
            .ConfigureAwait(false);
        var outcome = consumeResult.Status switch
        {
            InboxConsumeStatus.Processed => DeadLetterReplayOutcomes.Processed,
            InboxConsumeStatus.AlreadyProcessed => DeadLetterReplayOutcomes.AlreadyProcessed,
            _ => throw new InvalidOperationException(
                $"Unsupported inbox consume status '{consumeResult.Status}'."),
        };

        await domainAuditWriter.WriteAsync(
                new MessagingDomainAuditWrite(
                    MessagingDomainAuditActionKeys.DeadLetterReplay,
                    request.MessageId,
                    outbox.TenantId,
                    MessagingDomainAuditOutcomes.Success,
                    ActorUserId: null,
                    ActorDisplayName: null,
                    DiffSummaryJson: JsonSerializer.Serialize(
                        new
                        {
                            consumerName = request.ConsumerName,
                            messageId = request.MessageId,
                            outcome,
                        },
                        JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<DeadLetterReplayResponse>.Success(
            new DeadLetterReplayResponse(request.MessageId, request.ConsumerName, outcome));
    }
}
