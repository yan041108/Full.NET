using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Full.NET.Modules.Messaging.Serialization;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;

namespace Full.NET.Modules.Messaging.Features.ReplayDeadLetter;

/// <summary>
/// 重放单条消费死信：在命令事务内重建事件信封并通过消费 Dispatcher 重新投递，依赖消费 Inbox 幂等。
/// </summary>
/// <remarks>
/// 重放只触发既定消费业务的幂等副作用，消费端以 <c>(ConsumerName, MessageId)</c> Inbox 去重，
/// 重复重放返回 <see cref="DeadLetterReplayOutcomes.AlreadyProcessed"/> 而不产生重复业务写入。
/// 重放与领域审计同事务原子写入；死信或 Outbox 信封缺失、订阅路由未登记时返回对应错误。
/// </remarks>
internal sealed class DeadLetterReplayService(
    IQueryExecutor queryExecutor,
    ICommandTransaction transaction,
    IntegrationEventConsumerDispatcher dispatcher,
    IntegrationEventSubscriptionCatalog catalog,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 重放单条消费死信：在命令事务内定位死信与 Outbox 信封，重建事件并经消费 Dispatcher 重新投递。
    /// </summary>
    /// <param name="request">重放请求，按消费者名与消息标识定位死信。</param>
    /// <param name="cancellationToken">用于取消数据库与消费操作的令牌。</param>
    /// <returns>重放结果，<c>Outcome</c> 标记本次是首次处理还是幂等重复。</returns>
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
                MessagingSqlParameters.Create(
                    ("ConsumerName", request.ConsumerName),
                    ("MessageId", request.MessageId)),
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
                MessagingSqlParameters.Create(("Id", request.MessageId)),
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
                        new DeadLetterReplayAuditDiff(
                            request.ConsumerName,
                            request.MessageId,
                            outcome),
                        MessagingJsonSerializerContext.Default.DeadLetterReplayAuditDiff)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<DeadLetterReplayResponse>.Success(
            new DeadLetterReplayResponse(request.MessageId, request.ConsumerName, outcome));
    }
}
