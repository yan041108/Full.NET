using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Messaging.Features.ManageOperationalDeadLetters;

/// <summary>Host 死信用例；消息内容与身份来自持久化和认证上下文，恢复与领域审计同事务。</summary>
internal sealed class OperationalDeadLetterService(IOutboxDeadLetterStore legacy, KafkaDeadLetterStore kafka,
    IEventStreamOwnershipGate gate, IEffectiveEventDeliveryOwnerResolver owners,
    IntegrationEventSubscriptionCatalog catalog, IntegrationEventConsumerDispatcher dispatcher,
    ICommandTransaction transaction, ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> audit,
    IClock clock, ICurrentTenantContextWriter tenant, IHttpContextAccessor httpContext)
{
    public async Task<Result<PagedResult<OperationalDeadLetterResponse>>> ListAsync(bool isKafka, DeadLetterFilter filter,
        CancellationToken cancellationToken)
    {
        if (ValidateFilter(filter) is { } error) return Result<PagedResult<OperationalDeadLetterResponse>>.Failure(error);
        if (isKafka)
        {
            var page = await kafka.ListAsync(filter, cancellationToken);
            return Result<PagedResult<OperationalDeadLetterResponse>>.Success(new(page.Items.Select(Map).ToArray(),
                page.Page, page.PageSize, page.Total));
        }
        var result = await legacy.ListAsync(filter, cancellationToken);
        return Result<PagedResult<OperationalDeadLetterResponse>>.Success(new(result.Items.Select(Map).ToArray(),
            result.Page, result.PageSize, result.Total));
    }

    public async Task<Result<OperationalDeadLetterResponse>> GetAsync(bool isKafka, Guid id, CancellationToken cancellationToken)
    {
        var value = isKafka ? MapNullable(await kafka.FindAsync(id, cancellationToken))
            : MapNullable(await legacy.FindAsync(id, cancellationToken));
        return value is null ? Result<OperationalDeadLetterResponse>.Failure(NotFound())
            : Result<OperationalDeadLetterResponse>.Success(value);
    }

    public Task<Result<RecoverDeadLetterResponse>> RecoverAsync(bool isKafka, Guid id, RecoverDeadLetterRequest request,
        CancellationToken cancellationToken)
    {
        var actor = MessagingOperationActor.Resolve(httpContext);
        if (MessagingOperationActor.Validate(actor, request.Reason) is { } error)
            return Task.FromResult(Result<RecoverDeadLetterResponse>.Failure(error));
        return transaction.ExecuteResultAsync(token => RecoverCoreAsync(isKafka, id, request.Reason.Trim(), actor!, token), cancellationToken);
    }

    private async Task<Result<RecoverDeadLetterResponse>> RecoverCoreAsync(bool isKafka, Guid id, string reason,
        MessagingOperationActor actor, CancellationToken cancellationToken)
    {
        Guid? tenantId;
        Guid? messageId;
        string? consumer = null;
        string? sourceTopic = null;
        int? sourcePartition = null;
        long? sourceOffset = null;
        string outcome;
        if (!isKafka)
        {
            var row = await legacy.FindAsync(id, cancellationToken);
            if (row is null) return Result<RecoverDeadLetterResponse>.Failure(NotFound());
            // 已登记流与切流共享独占事务门；未登记流不允许切流，解析器仍按 Legacy 规则处理。
            await gate.AcquireOwnershipChangeAsync(row.MessageType, row.SchemaVersion, cancellationToken);
            var owner = await owners.GetDeliveryOwnerAsync(row.MessageType, row.SchemaVersion, cancellationToken);
            if (owner is not (EventDeliveryOwner.LegacyPolling or EventDeliveryOwner.ShadowCdc))
                return Failure("messaging.dead_letter.owner_conflict", "事件流当前不允许 Legacy 回队。");
            if (!await legacy.TryRequeueAsync(id, clock.UtcNow, cancellationToken))
                return Failure("messaging.dead_letter.state_conflict", "消息已被其他操作处理或回队。");
            tenantId = row.TenantId;
            messageId = row.Id;
            outcome = "requeued";
        }
        else
        {
            var row = await kafka.FindAsync(id, cancellationToken);
            if (row is null) return Result<RecoverDeadLetterResponse>.Failure(NotFound());
            if (!row.CanReplay)
                return Failure("messaging.dead_letter.not_replayable", "信封损坏或缺少消费者，须先核查来源，不能执行重放。");
            var snapshot = DeadLetterMessageSnapshot.FromJson(row.SnapshotJson);
            if (snapshot.Envelope is null || snapshot.Envelope.EventId != row.MessageId)
                return Failure("messaging.dead_letter.snapshot_invalid", "持久化信封与消息身份不一致。");
            IIntegrationEventSubscription subscription;
            try { subscription = catalog.GetRequired(row.ConsumerName, snapshot.Envelope.MessageType, snapshot.Envelope.SchemaVersion); }
            catch (InvalidOperationException) { return Failure(MessagingErrorCodes.SubscriptionRouteNotFound, "消费者路由尚未登记。"); }
            var owner = await owners.GetDeliveryOwnerAsync(snapshot.Envelope.MessageType, snapshot.Envelope.SchemaVersion, cancellationToken);
            if (owner is not EventDeliveryOwner.CdcKafka)
                return Failure("messaging.dead_letter.owner_conflict", "事件流当前不允许 Kafka 消费重放。");
            InboxConsumeResult consumed;
            try { consumed = await dispatcher.ConsumeAsync(row.ConsumerName, snapshot.Envelope.ToEnvelope(), subscription, cancellationToken); }
            finally { tenant.SetHost(); }
            // Dispatcher 的 Inbox 唯一键承担并发幂等；两个不同 DLQ 位置也不会重复产生业务副作用。
            outcome = consumed.Status == InboxConsumeStatus.AlreadyProcessed ? "already_processed" : "processed";
            await kafka.MarkReplayedAsync(id, clock.UtcNow, cancellationToken);
            tenantId = row.TenantId;
            messageId = row.MessageId;
            consumer = row.ConsumerName;
            sourceTopic = row.SourceTopic;
            sourcePartition = row.SourcePartition;
            sourceOffset = row.SourceOffset;
        }
        var summary = new OperationalDeadLetterAuditDiff(isKafka ? "kafka" : "legacy", id, messageId,
            consumer, sourceTopic, sourcePartition, sourceOffset, reason, outcome);
        await audit.WriteAsync(new MessagingDomainAuditWrite(isKafka ? "messaging.dead_letters.replay" : "messaging.outbox_dead_letters.requeue",
            id, tenantId, "success", actor.UserId, actor.DisplayName,
            JsonSerializer.Serialize(summary, MessagingJsonSerializerContext.Default.OperationalDeadLetterAuditDiff)), cancellationToken);
        return Result<RecoverDeadLetterResponse>.Success(new(id, outcome));
    }

    private static Error? ValidateFilter(DeadLetterFilter f) => f.Page < 1 || f.Page > 1_000_000
        || f.PageSize < 1 || f.PageSize > 100 || f.SchemaVersion is <= 0 || f.MessageType?.Length > 256
        || f.ConsumerName?.Length > 200 || f.ErrorCode?.Length > 128 || f.FromUtc > f.ToUtc
            ? new Error("messaging.dead_letter.filter_invalid", "分页、筛选或时间范围无效。", ErrorType.Validation) : null;

    private static Error NotFound() => new(MessagingErrorCodes.DeadLetterNotFound, "未找到可恢复的死信。", ErrorType.NotFound);
    private static Result<RecoverDeadLetterResponse> Failure(string code, string title) =>
        Result<RecoverDeadLetterResponse>.Failure(new Error(code, title, ErrorType.Conflict));
    private static OperationalDeadLetterResponse? MapNullable(KafkaDeadLetterRow? r) => r is null ? null : Map(r);
    private static OperationalDeadLetterResponse? MapNullable(OutboxDeadLetter? r) => r is null ? null : Map(r);
    private static OperationalDeadLetterResponse Map(OutboxDeadLetter r) => new(r.Id, "legacy", null, r.Id,
        r.MessageType, r.SchemaVersion, r.TenantId, r.Attempts, r.ErrorCode, r.FailedAtUtc, r.FailedAtUtc,
        null, null, null, null, null, null, true, null);
    private static OperationalDeadLetterResponse Map(KafkaDeadLetterRow r) => new(r.Id, "kafka", r.ConsumerName,
        r.MessageId, r.MessageType, r.SchemaVersion, r.TenantId, r.Attempts, r.ErrorCode, r.FirstFailedAtUtc,
        r.LastFailedAtUtc, r.SourceTopic, r.SourcePartition, r.SourceOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), r.DlqTopic, r.DlqPartition,
        r.DlqOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), r.CanReplay, r.ReplayedAtUtc);
}

/// <summary>受控恢复的审计摘要，不保存消息载荷或客户端提交的操作者。</summary>
internal sealed record OperationalDeadLetterAuditDiff(string Source, Guid DeadLetterId, Guid? MessageId,
    string? ConsumerName, string? SourceTopic, int? SourcePartition, long? SourceOffset, string Reason, string Outcome);
