using System.Globalization;
using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

/// <summary>持久重放控制器；控制事务不包含 Broker 或 Inbox 副作用，未知提交通过新作用域读取权威状态。</summary>
internal sealed class KafkaReplayOperationService(KafkaReplayOperationStore store, ITrackedKafkaReplayService replay,
    KafkaReplayExecutionPolicy policy, IIntegrationEventSubscriptionCatalog catalog, IEffectiveEventDeliveryOwnerResolver owners,
    IDatabaseSessionLock sessionLock, ICommandTransaction transaction, IIdGenerator ids, IClock clock,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> audit, IServiceScopeFactory scopes, IHttpContextAccessor context)
{
    internal async Task<Result<KafkaReplayOperationResponse>> CreateAsync(CreateKafkaReplayOperationRequest request, CancellationToken token)
    {
        var actor = MessagingOperationActor.Resolve(context);
        if (MessagingOperationActor.Validate(actor, request.Reason) is { } actorError) return Result<KafkaReplayOperationResponse>.Failure(actorError);
        if (!policy.Enabled) return Unavailable();
        KafkaReplayRequest replayRequest;
        try { replayRequest = ParseRequest(request); }
        catch (ArgumentException exception) { return Invalid(exception.Message); }
        if (request.RequestId == Guid.Empty) return Invalid("A non-empty request identifier is required.");
        if (replayRequest.MaxMessages > policy.MaximumSynchronousMessages)
            return Result<KafkaReplayOperationResponse>.Failure(new(MessagingErrorCodes.KafkaReplaySynchronousLimitExceeded,
                "The per-execution message limit exceeds this deployment policy.", ErrorType.Validation));
        IntegrationEventTopicDefinition topic;
        try { topic = catalog.GetTopicByCodeRequired(replayRequest.TopicCode); }
        catch (InvalidOperationException) { return Invalid("The replay topic is not registered."); }
        if (!catalog.GetAllSubscriptions().Any(item => item.ConsumerName == replayRequest.ReplayConsumerName
            && item.EventType == topic.EventType && item.SchemaVersion == topic.SchemaVersion))
            return Invalid("The replay subscription does not match the registered topic.");
        var canonical = request with
        {
            FromOffset = replayRequest.FromOffset?.ToString(CultureInfo.InvariantCulture),
            ToOffset = replayRequest.ToOffset?.ToString(CultureInfo.InvariantCulture),
            TopicCode = topic.TopicCode, Partitions = replayRequest.Partitions.Order().ToArray(), Reason = replayRequest.Reason,
        };
        var json = JsonSerializer.Serialize(canonical, MessagingJsonSerializerContext.Default.CreateKafkaReplayOperationRequest);
        await using var creation = await sessionLock.TryAcquireAsync("messaging.replay.create." + request.RequestId.ToString("N"), token).ConfigureAwait(false);
        if (creation is null) return Conflict("Another request with this creation key is running.");
        return await transaction.ExecuteResultAsync(async ct =>
        {
            var existing = await store.FindRequestAsync(request.RequestId, ct).ConfigureAwait(false);
            if (existing is not null)
                return existing.OperatorUserId == actor!.UserId && existing.RequestJson == json
                    ? Result<KafkaReplayOperationResponse>.Success(Map(existing, clock.UtcNow))
                    : Conflict("The creation key belongs to a different request.");
            var now = clock.UtcNow;
            var row = new KafkaReplayOperationRow(ids.NewId(), request.RequestId, topic.EventType, topic.SchemaVersion,
                topic.TopicCode, replayRequest.ReplayConsumerName, json, null, KafkaReplayOperationStates.Prepared, 0,
                null, null, null, null, 0, 0, 0, null, actor!.UserId, actor.DisplayName, replayRequest.Reason, now, now);
            if (await store.InsertAsync(row, ct).ConfigureAwait(false) != 1) throw new InvalidOperationException("重放操作未能持久化。");
            await WriteAuditAsync(row, actor, "prepared", canonical.Reason, ct).ConfigureAwait(false);
            return Result<KafkaReplayOperationResponse>.Success(Map(row, now));
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<KafkaReplayOperationResponse>> FindAsync(Guid id, CancellationToken token) =>
        await store.FindAsync(id, token).ConfigureAwait(false) is { } row
            ? Result<KafkaReplayOperationResponse>.Success(Map(row, clock.UtcNow)) : NotFound();

    internal async Task<Result<PagedResult<KafkaReplayOperationResponse>>> ListAsync(int page, int pageSize, CancellationToken token)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
            return Result<PagedResult<KafkaReplayOperationResponse>>.Failure(new("messaging.replay_operation.request_invalid", "Invalid operation page.", ErrorType.Validation));
        var rows = await store.ListAsync(checked((page - 1) * pageSize), pageSize, token).ConfigureAwait(false);
        var total = await store.CountAsync(token).ConfigureAwait(false);
        return Result<PagedResult<KafkaReplayOperationResponse>>.Success(new(rows.Select(row => Map(row, clock.UtcNow)).ToArray(), page, pageSize, total));
    }

    /// <summary>同一操作只允许一个执行者；每次恢复仍有独立消息数/时间预算，不扩大原 Broker 范围。</summary>
    internal async Task<Result<KafkaReplayOperationResponse>> ExecuteAsync(Guid id, ChangeKafkaReplayOperationRequest request, CancellationToken token)
    {
        var actor = MessagingOperationActor.Resolve(context);
        if (MessagingOperationActor.Validate(actor, request.Reason) is { } actorError) return Result<KafkaReplayOperationResponse>.Failure(actorError);
        if (!policy.Enabled) return Unavailable();
        await using var execution = await sessionLock.TryAcquireAsync("messaging.replay." + id.ToString("N"), token).ConfigureAwait(false);
        if (execution is null) return Conflict("This replay operation has an active executor.");
        var row = await store.FindAsync(id, token).ConfigureAwait(false);
        if (row is null) return NotFound();
        if (row.State == KafkaReplayOperationStates.Completed) return Result<KafkaReplayOperationResponse>.Success(Map(row, clock.UtcNow));
        if ((row.State is KafkaReplayOperationStates.Running or KafkaReplayOperationStates.CancelRequested) && row.LeaseUntilUtc > clock.UtcNow)
            return Conflict("The previous execution lease is still active.");
        var original = JsonSerializer.Deserialize(row.RequestJson, MessagingJsonSerializerContext.Default.CreateKafkaReplayOperationRequest)
            ?? throw new InvalidOperationException("持久重放请求无效。");
        var parsed = ParseRequest(original);
        if (parsed.MaxMessages > policy.MaximumSynchronousMessages)
            return Invalid("The operation exceeds the current per-execution deployment policy.");
        IntegrationEventTopicDefinition topic;
        try { topic = catalog.GetTopicByCodeRequired(row.TopicCode); }
        catch (InvalidOperationException) { return Conflict("The recorded topic is no longer registered."); }
        if (topic.EventType != row.EventType || topic.SchemaVersion != row.SchemaVersion
            || !catalog.GetAllSubscriptions().Any(item => item.ConsumerName == row.ConsumerName && item.EventType == row.EventType && item.SchemaVersion == row.SchemaVersion)
            || await owners.GetDeliveryOwnerAsync(row.EventType, row.SchemaVersion, token).ConfigureAwait(false) != EventDeliveryOwner.CdcKafka)
            return Conflict("The recorded stream, subscription or delivery owner no longer matches.");
        var lease = ids.NewId();
        var now = clock.UtcNow;
        var until = now.Add(policy.ExecutionTimeout).AddSeconds(30);
        var admitted = await transaction.ExecuteResultAsync(async ct =>
        {
            if (await store.BeginAsync(row, lease, until, now, ct).ConfigureAwait(false) != 1) return Conflict("The operation changed concurrently.");
            await WriteAuditAsync(row with { AttemptCount = row.AttemptCount + 1 }, actor!, MessagingDomainAuditOutcomes.Requested, request.Reason, ct).ConfigureAwait(false);
            return Result<KafkaReplayOperationResponse>.Success(Map(row, now));
        }, token).ConfigureAwait(false);
        if (!admitted.IsSuccess) return admitted;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(policy.ExecutionTimeout);
        try
        {
            var progress = new KafkaReplayOperationProgress(scopes, id, lease, clock);
            await replay.ReplayTrackedAsync(parsed, ReadSnapshot(row), progress, deadline.Token).ConfigureAwait(false);
            // 终态和 B0 审计同事务；若提交响应丢失，catch 使用新连接重新确认，不能覆盖已经完成的结果。
            return await FinishInNewScopeAsync(id, lease, actor!, request.Reason, null, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var code = exception is KafkaReplayWindowUnavailableException ? "resource_or_window_changed"
                : exception is OperationCanceledException ? "cancelled_or_timed_out" : "execution_interrupted";
            var recovered = await FinishInNewScopeAsync(id, lease, actor!, request.Reason, code, cleanup.Token).ConfigureAwait(false);
            if (recovered.IsSuccess && recovered.Value!.State == KafkaReplayOperationStates.Completed) return recovered;
            if (exception is KafkaReplayWindowUnavailableException) return Conflict("The recorded Kafka resource or retained window is unavailable.");
            if (exception is ReplayOperationCancelledException) return recovered;
            throw;
        }
    }

    internal async Task<Result<KafkaReplayOperationResponse>> CancelAsync(Guid id, ChangeKafkaReplayOperationRequest request, CancellationToken token)
    {
        var actor = MessagingOperationActor.Resolve(context);
        if (MessagingOperationActor.Validate(actor, request.Reason) is { } error) return Result<KafkaReplayOperationResponse>.Failure(error);
        return await transaction.ExecuteResultAsync(async ct =>
        {
            var row = await store.FindAsync(id, ct).ConfigureAwait(false);
            if (row is null) return NotFound();
            if (row.State == KafkaReplayOperationStates.Completed) return Result<KafkaReplayOperationResponse>.Success(Map(row, clock.UtcNow));
            var next = row.State is KafkaReplayOperationStates.Running or KafkaReplayOperationStates.CancelRequested
                ? KafkaReplayOperationStates.CancelRequested
                : HasUnconfirmed(row) ? KafkaReplayOperationStates.ResultUnknown : KafkaReplayOperationStates.Cancelled;
            if (await store.CancelAsync(row, next, clock.UtcNow, ct).ConfigureAwait(false) != 1) return Conflict("The operation changed concurrently.");
            await WriteAuditAsync(row, actor!, next, request.Reason, ct).ConfigureAwait(false);
            return Result<KafkaReplayOperationResponse>.Success(Map(row with { State = next, LastReasonCode = "operator_cancelled", UpdatedAtUtc = clock.UtcNow }, clock.UtcNow));
        }, token).ConfigureAwait(false);
    }

    private async Task<Result<KafkaReplayOperationResponse>> FinishInNewScopeAsync(Guid id, Guid lease, MessagingOperationActor actor,
        string reason, string? failureCode, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<KafkaReplayOperationService>()
            .FinishUnderLeaseAsync(id, lease, actor, reason, failureCode, token).ConfigureAwait(false);
    }

    /// <summary>清理和终态均读取持久权威行；不沿用失败事务或内存计数推断成功。</summary>
    internal Task<Result<KafkaReplayOperationResponse>> FinishUnderLeaseAsync(Guid id, Guid lease, MessagingOperationActor actor,
        string reason, string? failureCode, CancellationToken token) => transaction.ExecuteResultAsync(async ct =>
    {
        var row = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (row is null) return NotFound();
        if (row.State == KafkaReplayOperationStates.Completed) return Result<KafkaReplayOperationResponse>.Success(Map(row, clock.UtcNow));
        if (row.LeaseToken != lease || row.State is not (KafkaReplayOperationStates.Running or KafkaReplayOperationStates.CancelRequested))
            return Conflict("A newer execution owns this operation.");
        var snapshot = ReadSnapshot(row);
        var complete = snapshot is not null && !HasUnconfirmed(row) && snapshot.Ranges.All(item => item.NextOffset == item.EndOffsetExclusive);
        var state = failureCode == "resource_or_window_changed" ? KafkaReplayOperationStates.Blocked
            : complete && failureCode is null ? KafkaReplayOperationStates.Completed
            : HasUnconfirmed(row) || failureCode == "execution_interrupted" ? KafkaReplayOperationStates.ResultUnknown
            : row.State == KafkaReplayOperationStates.CancelRequested || failureCode == "cancelled_or_timed_out" ? KafkaReplayOperationStates.Cancelled
            : KafkaReplayOperationStates.Partial;
        if (await store.FinishAsync(row, state, failureCode, clock.UtcNow, failureCode is null, ct).ConfigureAwait(false) != 1)
            return Conflict("The execution lease or state changed before its terminal commit.");
        await WriteAuditAsync(row, actor, state, reason, ct).ConfigureAwait(false);
        return Result<KafkaReplayOperationResponse>.Success(Map(row with { State = state, LastReasonCode = failureCode,
            LeaseUntilUtc = null, UpdatedAtUtc = clock.UtcNow }, clock.UtcNow));
    }, token);

    private Task WriteAuditAsync(KafkaReplayOperationRow row, MessagingOperationActor actor, string outcome, string reason, CancellationToken token) =>
        audit.WriteAsync(new MessagingDomainAuditWrite(MessagingDomainAuditActionKeys.KafkaRangeReplay, row.Id, null, outcome is "prepared" or "requested" or "cancel_requested" ? MessagingDomainAuditOutcomes.Requested
                : outcome is "blocked" or "result_unknown" ? MessagingDomainAuditOutcomes.Failure : MessagingDomainAuditOutcomes.Success,
            actor.UserId, actor.DisplayName, JsonSerializer.Serialize(new KafkaReplayOperationAuditDiff(row.Id, row.AttemptCount,
                outcome, reason, row.ProcessedCount, row.AlreadyProcessedCount, row.RejectedCount),
                MessagingJsonSerializerContext.Default.KafkaReplayOperationAuditDiff)), token);

    internal static KafkaReplayRequest ParseRequest(CreateKafkaReplayOperationRequest request) => new(request.TopicCode,
        request.FromTimestampUtc, request.ToTimestampUtc, Offset(request.FromOffset), Offset(request.ToOffset),
        request.Partitions, request.ReplayConsumerName, request.MaxMessages, request.Reason);

    private static long? Offset(string? value) => value is null ? null : value.Length is > 0 and <= 19
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ? offset
        : throw new ArgumentException("Replay offsets must be non-negative decimal Int64 strings.");

    internal static KafkaReplaySnapshot? ReadSnapshot(KafkaReplayOperationRow row) => row.SnapshotJson is null ? null
        : JsonSerializer.Deserialize(row.SnapshotJson, MessagingJsonSerializerContext.Default.KafkaReplaySnapshot)
            ?? throw new InvalidOperationException("持久重放窗口无效。");

    internal static bool HasUnconfirmed(KafkaReplayOperationRow row) => row.InFlightOffset is not null
        || ReadSnapshot(row)?.Ranges.Any(item => item.UnconfirmedOffset is not null) == true;

    internal static KafkaReplayOperationResponse Map(KafkaReplayOperationRow row, DateTimeOffset now) => new(row.Id,
        (row.State is KafkaReplayOperationStates.Running or KafkaReplayOperationStates.CancelRequested) && (row.LeaseUntilUtc is null || row.LeaseUntilUtc <= now)
            ? KafkaReplayOperationStates.ResultUnknown : row.State,
        row.TopicCode, row.ConsumerName, row.EventType, row.SchemaVersion, ParseRequest(JsonSerializer.Deserialize(row.RequestJson,
            MessagingJsonSerializerContext.Default.CreateKafkaReplayOperationRequest)!).MaxMessages,
        row.ProcessedCount + row.AlreadyProcessedCount, row.ProcessedCount, row.AlreadyProcessedCount, row.RejectedCount,
        row.AttemptCount, HasUnconfirmed(row), row.LastReasonCode, row.CreatedAtUtc, row.UpdatedAtUtc,
        ReadSnapshot(row)?.Ranges.Select(item => new KafkaReplayPartitionResponse(item.Partition,
            item.StartOffset.ToString(CultureInfo.InvariantCulture), item.EndOffsetExclusive.ToString(CultureInfo.InvariantCulture),
            item.NextOffset.ToString(CultureInfo.InvariantCulture))).ToArray() ?? []);

    private static Result<KafkaReplayOperationResponse> Invalid(string message) => Result<KafkaReplayOperationResponse>.Failure(
        new(MessagingErrorCodes.KafkaReplayRequestInvalid, message, ErrorType.Validation));
    private static Result<KafkaReplayOperationResponse> Conflict(string message) => Result<KafkaReplayOperationResponse>.Failure(
        new("messaging.replay_operation.state_conflict", message, ErrorType.Conflict));
    private static Result<KafkaReplayOperationResponse> NotFound() => Result<KafkaReplayOperationResponse>.Failure(
        new("messaging.replay_operation.not_found", "Replay operation was not found.", ErrorType.NotFound));
    private static Result<KafkaReplayOperationResponse> Unavailable() => Result<KafkaReplayOperationResponse>.Failure(
        new(MessagingErrorCodes.KafkaReplayUnavailable, "Kafka replay is disabled for this deployment.", ErrorType.BusinessRule));
}

/// <summary>每代执行的审计摘要；原始请求、载荷和内部资源身份不写入摘要。</summary>
internal sealed record KafkaReplayOperationAuditDiff(Guid OperationId, int Attempt, string State, string Reason,
    long ProcessedMessages, long AlreadyProcessedMessages, long RejectedAttempts);

/// <summary>已收到持久取消请求，在下次业务副作用前停止。</summary>
internal sealed class ReplayOperationCancelledException : OperationCanceledException;
