using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Features.ManageShadowCapture;

/// <summary>有界采集控制；与切流共享控制面锁，并先取得所有权排他锁等待在途 Legacy 事务结束。</summary>
internal sealed class ShadowCaptureService(ShadowCaptureStore store, IOptions<ShadowCaptureOptions> options,
    IIntegrationEventSubscriptionCatalog catalog, IEventStreamOwnershipStore ownership, IEventStreamOwnershipGate gate,
    IDatabaseSessionLock sessionLock, ICommandTransaction transaction, IIdGenerator ids, IClock clock,
    ITransactionalDomainAuditWriter<MessagingDomainAuditWrite> audit, IHttpContextAccessor context, ICurrentTenant tenant)
{
    internal async Task<Result<ShadowCaptureResponse>> FindAsync(string eventType, int version, CancellationToken token)
    {
        EnsureHost();
        if (!ValidStream(eventType, version)) return Invalid();
        var row = await store.FindAsync(eventType, version, token).ConfigureAwait(false);
        return row is null ? NotFound() : Result<ShadowCaptureResponse>.Success(Map(row, clock.UtcNow));
    }

    internal async Task<Result<ShadowCaptureResponse>> StartAsync(StartShadowCaptureRequest request, CancellationToken token)
    {
        EnsureHost();
        var actor = MessagingOperationActor.Resolve(context);
        if (MessagingOperationActor.Validate(actor, request.Reason) is { } error) return Result<ShadowCaptureResponse>.Failure(error);
        var policy = options.Value;
        if (!policy.Enabled) return Failure("messaging.shadow_capture.unavailable", "Shadow capture is disabled.", ErrorType.BusinessRule);
        if (!ValidStream(request.EventType, request.SchemaVersion) || request.EventType != policy.EventType || request.SchemaVersion != policy.SchemaVersion
            || request.RequestId == Guid.Empty || request.ExpectedGeneration == Guid.Empty
            || request.MaxEvents < 1 || request.MaxEvents > policy.MaximumEvents || request.WindowSeconds < 1 || request.WindowSeconds > policy.MaximumWindowSeconds)
            return Invalid();
        IntegrationEventTopicDefinition topic;
        try { topic = catalog.GetTopicRequired(request.EventType, request.SchemaVersion); }
        catch (InvalidOperationException) { return Invalid(); }
        await using var control = await sessionLock.TryAcquireAsync(EventDeliveryControlResources.SessionLock, token).ConfigureAwait(false);
        if (control is null) return Conflict();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await LegacyLockedAsync(topic.EventType, topic.SchemaVersion, ct, topic.TopicCode).ConfigureAwait(false)) return Conflict();
            var current = await store.FindAsync(request.EventType, request.SchemaVersion, ct).ConfigureAwait(false);
            if (current?.StartRequestId == request.RequestId)
                return current.StartExpectedGeneration == request.ExpectedGeneration && current.OperatorUserId == actor!.UserId && current.MaxEvents == request.MaxEvents
                    && current.Reason == request.Reason.Trim() && current.ExpiresAtUtc - current.StartedAtUtc == TimeSpan.FromSeconds(request.WindowSeconds)
                    ? Result<ShadowCaptureResponse>.Success(Map(current, clock.UtcNow)) : Conflict();
            if (current?.Generation != request.ExpectedGeneration || current is { State: "active" } && current.ExpiresAtUtc > clock.UtcNow)
                return Conflict();
            var now = clock.UtcNow;
            var next = new ShadowCaptureRow(current?.Id ?? ids.NewId(), ids.NewId(), request.RequestId, request.EventType, request.SchemaVersion,
                "active", request.MaxEvents, policy.MaximumPayloadBytes, 0, 0, 0, null, now, now.AddSeconds(request.WindowSeconds), null, null,
                actor!.UserId, actor.DisplayName, request.Reason.Trim(), current?.CreatedAtUtc ?? now, now, request.ExpectedGeneration);
            var affected = current is null ? await store.InsertAsync(next, ct).ConfigureAwait(false) : await store.ReplaceAsync(current, next, ct).ConfigureAwait(false);
            if (affected != 1) return Conflict();
            await WriteAuditAsync(next, actor, "started", request.Reason, ct).ConfigureAwait(false);
            return Result<ShadowCaptureResponse>.Success(Map(next, now));
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<ShadowCaptureResponse>> CloseAsync(Guid generation, CloseShadowCaptureRequest request, CancellationToken token)
    {
        EnsureHost();
        var actor = MessagingOperationActor.Resolve(context);
        if (MessagingOperationActor.Validate(actor, request.Reason) is { } error) return Result<ShadowCaptureResponse>.Failure(error);
        await using var control = await sessionLock.TryAcquireAsync(EventDeliveryControlResources.SessionLock, token).ConfigureAwait(false);
        if (control is null) return Conflict();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            var hint = await store.FindGenerationAsync(generation, ct).ConfigureAwait(false);
            if (hint is null) return NotFound();
            if (!await LegacyLockedAsync(hint.MessageType, hint.SchemaVersion, ct).ConfigureAwait(false)) return Conflict();
            var row = await store.FindGenerationAsync(generation, ct).ConfigureAwait(false);
            if (row is null) return NotFound();
            if (row.State != "active") return Result<ShadowCaptureResponse>.Success(Map(row, clock.UtcNow));
            var now = clock.UtcNow;
            if (await store.CloseAsync(row, now, ct).ConfigureAwait(false) != 1) return Conflict();
            row = row with { State = "closed", ClosedAtUtc = now, UpdatedAtUtc = now, EndReason = row.ExpiresAtUtc <= now ? "time_budget" : "operator_closed" };
            await WriteAuditAsync(row, actor!, "closed", request.Reason, ct).ConfigureAwait(false);
            return Result<ShadowCaptureResponse>.Success(Map(row, now));
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> LegacyLockedAsync(string eventType, int version, CancellationToken token, string? expectedTopic = null)
    {
        // 首次登记所有权属于独立前置；缺少持久行时不能证明旧生产者已全部退出，因此不能偷偷补行。
        if (!await gate.AcquireOwnershipChangeAsync(eventType, version, token).ConfigureAwait(false)) return false;
        var row = await ownership.FindAsync(eventType, version, token).ConfigureAwait(false);
        return row?.CurrentOwner == EventDeliveryOwner.LegacyPolling
            && (expectedTopic is null || row.TopicCode == expectedTopic);
    }
    private void EnsureHost()
    {
        if (!tenant.IsAvailable || !tenant.IsHost) throw new HostContextRequiredException("messaging.shadow_capture.control");
    }
    private Task WriteAuditAsync(ShadowCaptureRow row, MessagingOperationActor actor, string phase, string reason, CancellationToken token) =>
        audit.WriteAsync(new(MessagingDomainAuditActionKeys.ShadowCaptureControl, row.Generation, null, MessagingDomainAuditOutcomes.Success, actor.UserId, actor.DisplayName,
            JsonSerializer.Serialize(new ShadowCaptureAuditDiff(row.Generation, row.MessageType, row.SchemaVersion, phase, row.CapturedCount, row.CapturedBytes, reason.Trim()),
                MessagingJsonSerializerContext.Default.ShadowCaptureAuditDiff)), token);
    internal static ShadowCaptureResponse Map(ShadowCaptureRow row, DateTimeOffset now) => new(row.Generation, row.MessageType, row.SchemaVersion,
        row.State == "active" && row.ExpiresAtUtc <= now ? "expired" : row.State, row.MaxEvents, row.MaxPayloadBytes,
        row.CapturedCount, row.CapturedBytes, row.MissingMetadataCount, row.StartedAtUtc, row.ExpiresAtUtc, row.ClosedAtUtc, row.EndReason);
    private static bool ValidStream(string? value, int version) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= MessagingNames.MessageTypeMaxLength && MessagingNames.MessageTypePattern.IsMatch(value) && version > 0;
    private static Result<ShadowCaptureResponse> Invalid() => Failure("messaging.shadow_capture.request_invalid", "Invalid shadow capture request.", ErrorType.Validation);
    private static Result<ShadowCaptureResponse> Conflict() => Failure("messaging.shadow_capture.state_conflict", "Capture generation changed, a window is active, or persisted Legacy ownership is unavailable.", ErrorType.Conflict);
    private static Result<ShadowCaptureResponse> NotFound() => Failure("messaging.shadow_capture.not_found", "Shadow capture was not found.", ErrorType.NotFound);
    private static Result<ShadowCaptureResponse> Failure(string code, string message, ErrorType type) => Result<ShadowCaptureResponse>.Failure(new(code, message, type));
}

/// <summary>采集控制的 B0 摘要，仅保留代次和有界计数。</summary>
internal sealed record ShadowCaptureAuditDiff(Guid Generation, string EventType, int SchemaVersion, string Phase,
    int CapturedCount, long CapturedBytes, string Reason);
