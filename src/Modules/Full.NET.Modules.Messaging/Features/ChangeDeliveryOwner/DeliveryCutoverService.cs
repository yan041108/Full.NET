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
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

/// <summary>
/// 将指定事件流的交付所有权从 Legacy 轮询切换到 CDC Kafka，并通过 CAS 守卫避免并发双发布。
/// </summary>
/// <remarks>
/// 切流属高风险运维操作，必须满足以下前置条件：环境已启用切流、所有者行已持久化、
/// 当前生效所有者为 Legacy 轮询、Legacy Outbox 积压与死信已排空、目标流无活动租约或到期重试。
/// 所有权 Upsert 以乐观版本号做 CAS 并发控制，并发冲突时返回错误而非静默覆盖；
/// 切流与领域审计在同一事务原子写入，确保切流动作可追溯、可回滚。
/// </remarks>
internal sealed class DeliveryCutoverService(
    IOutboxBacklogReader backlogReader,
    IntegrationEventSubscriptionCatalog catalog,
    IEffectiveEventDeliveryOwnerResolver ownerResolver,
    IEventStreamOwnershipGate ownershipGate,
    IOptions<DeliveryCutoverOptions> cutoverOptions,
    EventStreamOwnershipStore ownershipStore,
    IIdGenerator idGenerator,
    IClock clock,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> domainAuditWriter,
    DeliveryCutoverEvidenceService evidenceService,
    IDatabaseSessionLock sessionLock,
    IEnumerable<IntegrationEventTopicDefinition> registeredTopics,
    Microsoft.AspNetCore.Http.IHttpContextAccessor? httpContextAccessor = null)
{
    /// <summary>
    /// 执行交付所有权切换：校验前置条件后，在命令事务内 CAS 写入新所有者、记录切流边界并写领域审计。
    /// </summary>
    /// <param name="request">切流请求，必须包含事件类型、版本、目标所有者与运维理由。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>切流结果；前置条件不满足或 CAS 并发冲突时返回对应错误，成功时返回切流边界事件。</returns>
    public async Task<Result<DeliveryCutoverResponse>> CutoverAsync(
        ChangeDeliveryOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (MessagingOperationActor.Validate(MessagingOperationActor.Resolve(httpContextAccessor), request.Reason) is { } actorError)
            return Result<DeliveryCutoverResponse>.Failure(actorError);
        if (!cutoverOptions.Value.Enabled)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.CutoverPreconditionFailed,
                "CDC/Kafka delivery cutover is disabled for this environment.",
                ErrorType.BusinessRule));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.ReasonRequired,
                "A cutover reason is required.",
                ErrorType.Validation));
        }

        if (request.TargetOwner is not EventDeliveryOwner.CdcKafka)
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.InvalidCutoverTarget,
                "Only CDC Kafka ownership can be requested through this endpoint.",
                ErrorType.BusinessRule));
        }

        await using var control = await sessionLock.TryAcquireAsync(EventDeliveryControlResources.SessionLock, cancellationToken).ConfigureAwait(false);
        if (control is null) return Result<DeliveryCutoverResponse>.Failure(new Error(MessagingErrorCodes.CutoverPreconditionFailed,
            "Another delivery control operation is running.", ErrorType.Conflict));
        // 外部探测在事务外执行；命令事务内只重验短期证明及权威数据摘要。
        var evidence = await evidenceService.FindReadyAsync(request.EventType, request.SchemaVersion, cancellationToken).ConfigureAwait(false);
        if (evidence is null) return Result<DeliveryCutoverResponse>.Failure(new Error(MessagingErrorCodes.CutoverPreconditionFailed,
            "Fresh durable shadow evidence and live target resources are required.", ErrorType.BusinessRule));
        return await transaction.ExecuteResultAsync(
            token => CutoverCoreAsync(request, evidence, token), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<DeliveryCutoverResponse>> CutoverCoreAsync(
        ChangeDeliveryOwnerRequest request,
        CutoverEvidenceRow evidence,
        CancellationToken cancellationToken)
    {
        if (!await ownershipGate.AcquireOwnershipChangeAsync(
                request.EventType,
                request.SchemaVersion,
                cancellationToken).ConfigureAwait(false))
        {
            return Result<DeliveryCutoverResponse>.Failure(new Error(
                MessagingErrorCodes.CutoverPreconditionFailed,
                "The event stream has no persisted ownership row and cannot be cut over safely.",
                ErrorType.BusinessRule));
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

        // 当前固定 Topic 的专用 Connector 协议只支持一个活动试点流，不能把共享 Outbox 的其他流一起路由。
        var declaredOwners = await ownerResolver.GetDeliveryOwnersAsync(registeredTopics.ToArray(), cancellationToken).ConfigureAwait(false);
        var persistedOwners = await ownershipStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (declaredOwners.Any(item => item.Value == EventDeliveryOwner.CdcKafka && (item.Key.EventType != request.EventType || item.Key.SchemaVersion != request.SchemaVersion))
            || persistedOwners.Any(item => item.CurrentOwner == EventDeliveryOwner.CdcKafka && (item.MessageType != request.EventType || item.SchemaVersion != request.SchemaVersion)))
            return Result<DeliveryCutoverResponse>.Failure(new Error(MessagingErrorCodes.CutoverPreconditionFailed,
                "The current dedicated connector protocol supports only one active CDC pilot stream.", ErrorType.BusinessRule));

        if (evidence.ExpiresAtUtc <= clock.UtcNow || !await evidenceService.IsSourceUnchangedAsync(evidence, cancellationToken).ConfigureAwait(false))
            return Result<DeliveryCutoverResponse>.Failure(new Error(MessagingErrorCodes.CutoverPreconditionFailed,
                "Shadow evidence expired or its source data changed.", ErrorType.BusinessRule));

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
        var cutoff = await backlogReader
            .ReadLastStreamEventAsync(request.EventType, request.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        var cutoffEventId = cutoff?.EventId ?? Guid.Empty;
        var cutoffOccurredAtUtc = cutoff?.OccurredAtUtc ?? clock.UtcNow;
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
            MessagingOperationActor.Resolve(httpContextAccessor)!.UserId,
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
                    ActorUserId: MessagingOperationActor.Resolve(httpContextAccessor)!.UserId,
                    ActorDisplayName: MessagingOperationActor.Resolve(httpContextAccessor)!.DisplayName,
                    DiffSummaryJson: JsonSerializer.Serialize(
                        new DeliveryCutoverAuditDiff(
                            request.EventType,
                            request.SchemaVersion,
                            currentOwner.ToString(),
                            request.TargetOwner.ToString(),
                            cutoffEventId,
                            cutoffOccurredAtUtc,
                            request.Reason,
                            OwnershipPersisted: true,
                            evidence.Id),
                        MessagingJsonSerializerContext.Default.DeliveryCutoverAuditDiff)),
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
