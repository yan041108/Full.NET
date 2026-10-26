using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Messaging.Features.ReplayKafkaRange;

/// <summary>一代执行拥有一个进度接收器；每次回调另建 Host 事务，绝不与业务 Inbox 共用失败会话。</summary>
internal sealed class KafkaReplayOperationProgress(IServiceScopeFactory scopes, Guid operationId, Guid leaseToken, IClock clock)
    : IKafkaReplayProgressSink
{
    private bool _previouslyUnknown;

    public Task BindSnapshotAsync(KafkaReplaySnapshot snapshot, CancellationToken token) => UpdateAsync(async (store, row, ct) =>
    {
        var canonical = Serialize(snapshot);
        if (row.SnapshotJson is not null && row.SnapshotJson != canonical)
            throw new KafkaReplayWindowUnavailableException();
        await SaveAsync(store, row, row with { SnapshotJson = canonical }, false, ct).ConfigureAwait(false);
    }, token);

    public Task BeforeMessageAsync(int partition, long offset, CancellationToken token) => UpdateAsync(async (store, row, ct) =>
    {
        if (row.State == KafkaReplayOperationStates.CancelRequested) throw new ReplayOperationCancelledException();
        var snapshot = RequiredSnapshot(row);
        var range = Range(snapshot, partition);
        if (offset < range.NextOffset || offset >= range.EndOffsetExclusive
            || range.UnconfirmedOffset is { } unknown && unknown != offset)
            throw new KafkaReplayWindowUnavailableException();
        _previouslyUnknown = range.UnconfirmedOffset is not null;
        snapshot = Replace(snapshot, range with { UnconfirmedOffset = offset });
        await SaveAsync(store, row, row with { SnapshotJson = Serialize(snapshot), InFlightPartition = partition,
            InFlightOffset = offset }, true, ct).ConfigureAwait(false);
    }, token);

    public Task AfterMessageAsync(int partition, long offset, KafkaReplayConfirmedOutcome outcome, CancellationToken token) =>
        UpdateAsync(async (store, row, ct) =>
        {
            if (row.InFlightPartition != partition || row.InFlightOffset != offset)
                throw new InvalidOperationException("重放确认与持久执行意图不匹配。");
            var snapshot = RequiredSnapshot(row);
            var range = Range(snapshot, partition);
            if (range.UnconfirmedOffset != offset) throw new InvalidOperationException("重放分区意图已发生变化。");
            var next = row with { InFlightPartition = null, InFlightOffset = null };
            switch (outcome)
            {
                case KafkaReplayConfirmedOutcome.Processed:
                    next = next with { ProcessedCount = checked(row.ProcessedCount + 1) };
                    range = range with { NextOffset = checked(offset + 1), UnconfirmedOffset = null };
                    break;
                case KafkaReplayConfirmedOutcome.AlreadyProcessed:
                    next = next with { AlreadyProcessedCount = checked(row.AlreadyProcessedCount + 1) };
                    range = range with { NextOffset = checked(offset + 1), UnconfirmedOffset = null };
                    break;
                case KafkaReplayConfirmedOutcome.Rejected:
                    next = next with { RejectedCount = checked(row.RejectedCount + 1) };
                    // 拒绝本次尝试不能证明上一次结果未知的副作用未发生，原未知标记保留。
                    if (!_previouslyUnknown) range = range with { UnconfirmedOffset = null };
                    break;
                default: throw new InvalidOperationException("不支持的重放确认结果。");
            }
            await SaveAsync(store, row, next with { SnapshotJson = Serialize(Replace(snapshot, range)) }, false, ct).ConfigureAwait(false);
        }, token);

    public Task CompletePartitionAsync(int partition, long endOffsetExclusive, CancellationToken token) =>
        UpdateAsync(async (store, row, ct) =>
        {
            var snapshot = RequiredSnapshot(row);
            var range = Range(snapshot, partition);
            if (range.EndOffsetExclusive != endOffsetExclusive || range.UnconfirmedOffset is not null
                || row.InFlightPartition == partition && row.InFlightOffset is not null)
                throw new KafkaReplayWindowUnavailableException();
            // EOF 可以确认没有更多 Broker 记录，但不能抹去可能已经提交的未知业务结果。
            await SaveAsync(store, row, row with { SnapshotJson = Serialize(Replace(snapshot, range with { NextOffset = endOffsetExclusive })) }, false, ct)
                .ConfigureAwait(false);
        }, token);

    private async Task UpdateAsync(Func<KafkaReplayOperationStore, KafkaReplayOperationRow, CancellationToken, Task> action, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var store = scope.ServiceProvider.GetRequiredService<KafkaReplayOperationStore>();
        await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct =>
        {
            var row = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            if (row is null || row.LeaseToken != leaseToken || row.LeaseUntilUtc <= clock.UtcNow || row.LeaseUntilUtc is null
                || row.State is not (KafkaReplayOperationStates.Running or KafkaReplayOperationStates.CancelRequested))
                throw new InvalidOperationException("重放执行代次或租约已失效。");
            await action(store, row, ct).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
    }

    private async Task SaveAsync(KafkaReplayOperationStore store, KafkaReplayOperationRow previous, KafkaReplayOperationRow next,
        bool beforeMessage, CancellationToken token)
    {
        if (await store.SaveProgressAsync(previous, next, clock.UtcNow, beforeMessage, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("重放进度代次、取消状态或消息意图已变化。");
    }

    private static KafkaReplaySnapshot RequiredSnapshot(KafkaReplayOperationRow row) =>
        KafkaReplayOperationService.ReadSnapshot(row) ?? throw new InvalidOperationException("必须先固定重放窗口。");
    private static KafkaReplayPartitionRange Range(KafkaReplaySnapshot snapshot, int partition) =>
        snapshot.Ranges.SingleOrDefault(item => item.Partition == partition) ?? throw new KafkaReplayWindowUnavailableException();
    private static KafkaReplaySnapshot Replace(KafkaReplaySnapshot snapshot, KafkaReplayPartitionRange range) =>
        snapshot with { Ranges = snapshot.Ranges.Select(item => item.Partition == range.Partition ? range : item).ToArray() };
    private static string Serialize(KafkaReplaySnapshot snapshot) => JsonSerializer.Serialize(snapshot with
        { Ranges = snapshot.Ranges.OrderBy(item => item.Partition).ToArray() }, MessagingJsonSerializerContext.Default.KafkaReplaySnapshot);
}
