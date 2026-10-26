using System.Data.Common;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Features.GetDeliveryStatus;

/// <summary>有界运维总览；保留全局摘要和批量所有者，独立标注读取失败、目录缺失与尚未观测的 Broker 状态。</summary>
internal sealed class DeliveryStatusQueryService(IOutboxBacklogReader backlogReader,
    IEffectiveEventDeliveryOwnerResolver ownerResolver, IEnumerable<IntegrationEventTopicDefinition> topics,
    IQueryExecutor query, IOptions<DatabaseOptions> database, IOptions<DeliveryCutoverOptions> cutover,
    KafkaReplayExecutionPolicy replay, IEventDeliveryRollbackReadinessReader rollbackReader,
    IIntegrationEventSubscriptionCatalog subscriptions, IOptions<ShadowCaptureOptions> shadowCapture)
{
    private const int MaximumStreams = 512;
    private readonly IntegrationEventTopicDefinition[] _topics = topics.DistinctBy(item => (item.EventType, item.SchemaVersion)).ToArray();

    public async Task<Result<DeliveryStatusResponse>> GetAsync(CancellationToken token)
    {
        var global = await ObserveAsync(() => backlogReader.ReadBacklogAsync(token), token).ConfigureAwait(false);
        var legacy = await ObserveAsync(() => backlogReader.ReadStreamBacklogsAsync(MaximumStreams, token), token).ConfigureAwait(false);
        var owners = await ObserveAsync(() => ownerResolver.GetDeliveryOwnersAsync(_topics, token), token).ConfigureAwait(false);
        var observations = await ObserveAsync(() => query.QueryAsync<DeliveryStreamObservationRow>(database.Value.Provider == DatabaseProvider.SqlServer
            ? DeliveryStreamObservationSql.ReadSqlServer : DeliveryStreamObservationSql.ReadMySql,
            new Dictionary<string, object?> { ["MaxStreams"] = MaximumStreams + 2 }, token), token).ConfigureAwait(false);
        var legacyComplete = legacy is not null && legacy.Count <= MaximumStreams;
        var observationComplete = observations is not null && observations.Count(item => item.MessageType is not null) <= MaximumStreams;
        var legacyByStream = legacy?.Take(MaximumStreams).ToDictionary(item => (item.EventType, item.SchemaVersion));
        var observedByStream = observations?.Where(item => item.MessageType is not null && item.SchemaVersion > 0).Take(MaximumStreams)
            .ToDictionary(item => (item.MessageType!, item.SchemaVersion!.Value));
        var definitions = _topics.ToDictionary(item => (item.EventType, item.SchemaVersion));
        var consumers = subscriptions.GetAllSubscriptions().GroupBy(item => (item.EventType, item.SchemaVersion))
            .ToDictionary(group => group.Key, group => group.Select(item => item.ConsumerName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        var keys = definitions.Keys.Concat(legacyByStream?.Keys.AsEnumerable() ?? Enumerable.Empty<(string, int)>()).Concat(observedByStream?.Keys.AsEnumerable() ?? Enumerable.Empty<(string, int)>())
            .Distinct().OrderBy(item => item.Item1, StringComparer.Ordinal).ThenBy(item => item.Item2).ToArray();
        var more = keys.Length > MaximumStreams || legacy is { Count: > MaximumStreams } || !observationComplete && observations is not null;
        var streams = new List<EventStreamStatusResponse>();
        foreach (var key in keys.Take(MaximumStreams))
        {
            definitions.TryGetValue(key, out var definition);
            var legacyRow = legacyByStream?.GetValueOrDefault(key);
            var observed = observedByStream?.GetValueOrDefault(key);
            EventDeliveryOwner? owner = owners?.TryGetValue(key, out var effective) == true ? effective : null;
            var summary = legacyRow is not null ? Map(legacyRow.Backlog) : legacyComplete ? Map(new OutboxBacklogSnapshot(0, null)) : null;
            var reasons = new List<string>();
            var blocked = false;
            void Block(string code) { blocked = true; reasons.Add(code); }
            if (definition is null) Block("stream_not_registered");
            if (!consumers.ContainsKey(key)) Block("subscription_not_registered");
            if (!cutover.Value.Enabled) Block("cutover_disabled");
            if (owner is null) reasons.Add("ownership_unavailable");
            else if (owner != EventDeliveryOwner.LegacyPolling) Block("cutover_owner_unsupported");
            if (observed is null && !observationComplete) reasons.Add("control_state_unavailable");
            else if (observed?.HasPersistedOwnership != 1) Block("ownership_record_required");
            if (observed?.RollbackState == 1) Block("rollback_preparing");
            if (summary is null) reasons.Add("legacy_backlog_unavailable");
            else if (summary.PendingCount > 0 || summary.DeadLetterCount > 0 || summary.ActiveLeaseCount > 0 || summary.DueRetryCount > 0)
                Block("legacy_backlog_not_drained");
            // 总览不发起外部控制或重放，不把本地配置/投影/历史证明等同为真实运行就绪。
            reasons.Add("shadow_evidence_not_observed");
            reasons.Add("target_runtime_not_observed");
            var state = summary is not null && (observed is not null || observationComplete) && (definition is null || owner is not null)
                ? "available" : "partial";
            streams.Add(new(key.Item1, key.Item2, definition?.TopicCode, owner, definition is not null, consumers.GetValueOrDefault(key) ?? [], state, summary,
                observed?.RetainedEventCount ?? (observationComplete ? 0 : null),
                observed?.KafkaProjectedDeadLetterCount ?? (observationComplete ? 0 : null), observed?.OldestDeadLetterAtUtc,
                observed?.RollbackState == 1 ? observed.RollbackGeneration : null, blocked ? "blocked" : "unknown", reasons));
        }
        var observationState = global is null && legacy is null && owners is null && observations is null ? "failed"
            : global is null || legacy is null || owners is null || observations is null || more ? "partial" : "available";
        return Result<DeliveryStatusResponse>.Success(new(global is null ? null : Map(global), streams, observationState, more,
            observations?.Where(item => item.MessageType is null).Sum(item => item.KafkaProjectedDeadLetterCount),
            cutover.Value.Enabled, replay.Enabled, replay.MaximumSynchronousMessages, checked((int)Math.Ceiling(replay.ExecutionTimeout.TotalSeconds)),
            rollbackReader is not FailClosedEventDeliveryRollbackReadinessReader, "not_observed",
            shadowCapture.Value.Enabled ? new(true, shadowCapture.Value.EventType, shadowCapture.Value.SchemaVersion,
                shadowCapture.Value.MaximumEvents, shadowCapture.Value.MaximumPayloadBytes, shadowCapture.Value.MaximumWindowSeconds)
                : new(false, null, null, null, null, null)));
    }

    private static OutboxBacklogSummaryResponse Map(OutboxBacklogSnapshot value) => new(value.PendingCount, value.DueRetryCount,
        value.ActiveLeaseCount, value.DeadLetterCount, value.OldestOccurredAtUtc, value.OldestDeadLetteredAtUtc);

    private static async Task<T?> ObserveAsync<T>(Func<Task<T>> read, CancellationToken token) where T : class
    {
        try { return await read().ConfigureAwait(false); }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }
}
