using System.Text.Json;
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
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;

/// <summary>同一持久代次的恢复入口；重新读取权威状态后才允许恢复外部发布者。</summary>
internal sealed class DeliveryRollbackRecoveryService(
    DeliveryRollbackJournal journal, EventStreamOwnershipStore ownershipStore,
    IEventStreamOwnershipGate gate, IEventDeliveryRollbackReadinessReader reader,
    IDatabaseSessionLock sessionLock, ICommandTransaction transaction, IClock clock,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> audit, IHttpContextAccessor httpContextAccessor)
{
    internal async Task<Result<RollbackOperationResponse>> FindAsync(Guid generation, CancellationToken cancellationToken) =>
        await journal.FindAsync(generation, cancellationToken).ConfigureAwait(false) is { } operation
            ? Result<RollbackOperationResponse>.Success(ToResponse(operation)) : NotFound();

    internal async Task<Result<RollbackOperationResponse>> FindLatestAsync(string eventType, int schemaVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > MessagingNames.MessageTypeMaxLength
            || !MessagingNames.MessageTypePattern.IsMatch(eventType) || schemaVersion <= 0)
            return Result<RollbackOperationResponse>.Failure(new(MessagingErrorCodes.RollbackPreconditionFailed, "Invalid event stream.", ErrorType.Validation));
        var operation = await journal.FindLatestAsync(eventType, schemaVersion, cancellationToken).ConfigureAwait(false);
        return operation is null ? NotFound() : Result<RollbackOperationResponse>.Success(ToResponse(operation));
    }

    internal async Task<Result<RollbackOperationResponse>> RecoverAsync(Guid generation, RecoverRollbackRequest request,
        CancellationToken cancellationToken)
    {
        if (MessagingOperationActor.Validate(MessagingOperationActor.Resolve(httpContextAccessor), request.Reason) is { } error)
            return Result<RollbackOperationResponse>.Failure(error);
        await using var control = await sessionLock.TryAcquireAsync(EventDeliveryControlResources.SessionLock, cancellationToken).ConfigureAwait(false);
        if (control is null) return Failure("Another delivery control operation is running.");
        return await RecoverUnderLockAsync(generation, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Result<RollbackOperationResponse>> RecoverUnderLockAsync(Guid generation, string reason,
        CancellationToken cancellationToken)
    {
        var prepared = await transaction.ExecuteResultAsync(async token =>
        {
            var operation = await journal.FindAsync(generation, token).ConfigureAwait(false);
            if (operation is null) return NotFound();
            // 提交结果丢失后可能已是 Completed；绝不向已经回退的旧发布路径发送 resume。
            if (operation.Phase is EventDeliveryRollbackPhase.Completed or EventDeliveryRollbackPhase.Recovered)
                return Result<RollbackOperationResponse>.Success(ToResponse(operation));
            if (!await gate.AcquireOwnershipChangeAsync(operation.EventType, operation.SchemaVersion, token).ConfigureAwait(false))
                return Failure("Persisted ownership is unavailable; producer protection remains in place.");
            var owner = await ownershipStore.FindAsync(operation.EventType, operation.SchemaVersion, token).ConfigureAwait(false);
            var fence = await ownershipStore.FindRollbackPreparationAsync(operation.EventType, operation.SchemaVersion, token).ConfigureAwait(false);
            if (owner?.CurrentOwner != EventDeliveryOwner.CdcKafka || fence?.RollbackState != 1 || fence.RollbackGeneration != generation)
                return Failure("Ownership or generation differs; external recovery is forbidden.");
            if (operation.Phase != EventDeliveryRollbackPhase.Recovering &&
                !await journal.TransitionAsync(generation, operation.Phase, EventDeliveryRollbackPhase.Recovering, token).ConfigureAwait(false))
                return Failure("Rollback state changed concurrently.");
            var actor = MessagingOperationActor.Resolve(httpContextAccessor)!;
            await audit.WriteAsync(new MessagingDomainAuditWrite(MessagingDomainAuditActionKeys.DeliveryRollbackRecovery, generation,
                null, MessagingDomainAuditOutcomes.Requested, actor.UserId, actor.DisplayName,
                JsonSerializer.Serialize(new RollbackRecoveryAuditDiff(generation, operation.EventType, operation.SchemaVersion, reason),
                    MessagingJsonSerializerContext.Default.RollbackRecoveryAuditDiff)), token).ConfigureAwait(false);
            return Result<RollbackOperationResponse>.Success(ToResponse(operation with { Phase = EventDeliveryRollbackPhase.Recovering }));
        }, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess || prepared.Value!.Phase != EventDeliveryRollbackPhase.Recovering) return prepared;

        var active = prepared.Value;
        // 外部调用不占用业务事务；异常、超时或无法确认 RUNNING 时保留 Recovering 和生产者 fence。
        var recovering = await journal.FindAsync(generation, cancellationToken).ConfigureAwait(false);
        if (recovering?.HasPauseIntent == true || recovering?.ResourcesJson is not null)
            await reader.AbortAsync(active.EventType, active.SchemaVersion, generation, cancellationToken).ConfigureAwait(false);
        return await transaction.ExecuteResultAsync(async token =>
        {
            if (!await gate.AcquireOwnershipChangeAsync(active.EventType, active.SchemaVersion, token).ConfigureAwait(false))
                return Failure("Ownership is unavailable after external recovery.");
            if (!await ownershipStore.TryAbortRollbackPreparationAsync(active.EventType, active.SchemaVersion, generation, clock.UtcNow, token).ConfigureAwait(false)
                || !await journal.TransitionAsync(generation, EventDeliveryRollbackPhase.Recovering, EventDeliveryRollbackPhase.Recovered, token).ConfigureAwait(false))
                return Failure("Rollback generation changed; producer protection was not cleared.");
            var actor = MessagingOperationActor.Resolve(httpContextAccessor)!;
            await audit.WriteAsync(new MessagingDomainAuditWrite(MessagingDomainAuditActionKeys.DeliveryRollbackRecovery, generation,
                null, MessagingDomainAuditOutcomes.Success, actor.UserId, actor.DisplayName,
                JsonSerializer.Serialize(new RollbackRecoveryAuditDiff(generation, active.EventType, active.SchemaVersion, reason),
                    MessagingJsonSerializerContext.Default.RollbackRecoveryAuditDiff)), token).ConfigureAwait(false);
            return Result<RollbackOperationResponse>.Success(active with { Phase = EventDeliveryRollbackPhase.Recovered, UpdatedAtUtc = clock.UtcNow });
        }, cancellationToken).ConfigureAwait(false);
    }

    private static RollbackOperationResponse ToResponse(EventDeliveryRollbackOperation operation) =>
        new(operation.Id, operation.EventType, operation.SchemaVersion, operation.Phase, operation.CreatedAtUtc, operation.UpdatedAtUtc);
    private static Result<RollbackOperationResponse> Failure(string message) => Result<RollbackOperationResponse>.Failure(
        new Error(MessagingErrorCodes.RollbackPreconditionFailed, message, ErrorType.Conflict));

    private static Result<RollbackOperationResponse> NotFound() => Result<RollbackOperationResponse>.Failure(
        new Error(MessagingErrorCodes.RollbackOperationNotFound, "Rollback generation was not found.", ErrorType.NotFound));
}

/// <summary>恢复审计记录不包含控制地址和内部资源快照。</summary>
internal sealed record RollbackRecoveryAuditDiff(Guid Generation, string EventType, int SchemaVersion, string Reason);
