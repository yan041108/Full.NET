namespace Full.NET.LogConsumer;

/// <summary>在单个 Kafka 分区的一次分配期内计算逐项完成后的连续安全提交水位。</summary>
/// <remarks>
/// 本类型只供一个 Consumer Poll 循环串行调用，不执行 Broker Commit。调用方须在 ES 成功或
/// DLQ 的 acks=all 投递报告成功后分别调用对应方法；返回的水位仍需由 Broker 确认提交。
/// </remarks>
public sealed class PartitionDeliveryWatermark
{
    private readonly long _assignmentEpoch;
    private readonly int _maxPending;
    private readonly Queue<Pending> _observed = new();
    private readonly Dictionary<long, Pending> _pendingByOffset = [];
    private long? _lastObservedOffset;
    private long? _rejectedDeliveredOffset;
    private long? _safeNextOffset;
    private long? _committedNextOffset;
    private bool _revoked;
    private bool _deliveryOrderFaulted;

    /// <summary>创建一次分区分配的有界状态；epoch 由外层分区所有权协调器提供。</summary>
    /// <param name="assignmentEpoch">每次重新分配都递增的本地所有权代数。</param>
    /// <param name="maxPending">该分区最多保留的未形成连续安全水位的记录数。</param>
    public PartitionDeliveryWatermark(long assignmentEpoch, int maxPending)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentEpoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPending);
        _assignmentEpoch = assignmentEpoch;
        _maxPending = maxPending;
    }

    /// <summary>按 Consumer 实际交付顺序登记记录；Offset 数字空洞不表示记录丢失。</summary>
    /// <param name="offset">Kafka 实际交付的非负记录 Offset。</param>
    /// <param name="assignmentEpoch">当前分区分配代数。</param>
    /// <returns>接受则为 true；过期、乱序或达到容量上限时为 false。</returns>
    /// <remarks>
    /// 容量拒绝意味着 Consumer 已交付但未登记该 Offset；调用方必须暂停并原位重试。
    /// 拒绝期间又交付其他 Offset 或出现倒序，当前分配永久失败关闭，须撤销后重建。
    /// </remarks>
    public bool TryTrack(long offset, long assignmentEpoch)
    {
        if (!IsCurrent(assignmentEpoch)
            || offset < 0
            || offset == long.MaxValue)
        {
            return false;
        }

        if ((_lastObservedOffset is long last && offset <= last)
            || (_rejectedDeliveredOffset is long rejected && offset != rejected))
        {
            // 倒序或继续投递会使未登记的真实记录无法由一个 Offset 重试槽覆盖。
            _deliveryOrderFaulted = true;
            return false;
        }

        if (_observed.Count >= _maxPending)
        {
            // Consumer 已交付但本地未登记的 Offset 必须原位重试，不能跳到后续记录。
            _rejectedDeliveredOffset ??= offset;
            return false;
        }

        var pending = new Pending(offset);
        _observed.Enqueue(pending);
        _pendingByOffset.Add(offset, pending);
        _lastObservedOffset = offset;
        _rejectedDeliveredOffset = null;
        return true;
    }

    /// <summary>记录 ES Bulk 的逐项结果；Isolate 仅进入等待 DLQ ACK 状态。</summary>
    /// <param name="offset">此前由当前分配登记的 Offset。</param>
    /// <param name="assignmentEpoch">当前分区分配代数。</param>
    /// <param name="outcome">逐项 ES 结果。</param>
    /// <returns>状态被接受则为 true；过期或未知记录为 false。</returns>
    public bool TryApplySinkResult(long offset, long assignmentEpoch, BulkItemOutcome outcome)
    {
        if (!TryGetPending(offset, assignmentEpoch, out var pending)
            || pending.State != PendingState.Pending)
        {
            return false;
        }

        pending.State = outcome switch
        {
            BulkItemOutcome.Succeeded => PendingState.Completed,
            BulkItemOutcome.Retry => PendingState.Pending,
            BulkItemOutcome.Isolate => PendingState.AwaitingIsolation,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        AdvanceSafeWatermark();
        return true;
    }

    /// <summary>只在外层确认该记录已获得可靠 DLQ 投递报告后调用。</summary>
    /// <param name="offset">等待隔离的记录 Offset。</param>
    /// <param name="assignmentEpoch">当前分区分配代数。</param>
    /// <returns>隔离确认被接受则为 true；未等待隔离或分配已失效则为 false。</returns>
    public bool TryConfirmIsolation(long offset, long assignmentEpoch)
    {
        if (!TryGetPending(offset, assignmentEpoch, out var pending)
            || pending.State != PendingState.AwaitingIsolation)
        {
            return false;
        }

        pending.State = PendingState.Completed;
        AdvanceSafeWatermark();
        return true;
    }

    /// <summary>读取可提交的下一 Offset；Broker 提交失败后仍保留该水位供重试。</summary>
    /// <param name="assignmentEpoch">当前分区分配代数。</param>
    /// <returns>当前安全 nextOffset；尚无连续完成记录或所有权失效时为 null。</returns>
    public long? GetSafeNextOffset(long assignmentEpoch) =>
        IsCurrent(assignmentEpoch) ? _safeNextOffset : null;

    /// <summary>读取 Broker 已确认的提交水位，与仅在本地安全的水位区分。</summary>
    /// <param name="assignmentEpoch">当前分区分配代数。</param>
    /// <returns>Broker 已确认的 nextOffset；尚无确认或所有权失效时为 null。</returns>
    public long? GetCommittedNextOffset(long assignmentEpoch) =>
        IsCurrent(assignmentEpoch) ? _committedNextOffset : null;

    /// <summary>仅在 Broker 提交成功后记录已提交水位，禁止越过本地安全水位。</summary>
    /// <param name="nextOffset">Broker 已确认的下一 Offset。</param>
    /// <param name="assignmentEpoch">当前分区分配代数。</param>
    /// <returns>确认有效则为 true；过期、倒退或超出安全水位时为 false。</returns>
    public bool TryConfirmCommit(long nextOffset, long assignmentEpoch)
    {
        if (!IsCurrent(assignmentEpoch)
            || nextOffset < 0
            || _safeNextOffset is not long safe
            || nextOffset > safe
            || (_committedNextOffset is long committed && nextOffset < committed))
        {
            return false;
        }

        _committedNextOffset = nextOffset;
        return true;
    }

    /// <summary>撤销所有权后废弃所有待确认项和旧安全水位。</summary>
    /// <param name="assignmentEpoch">被撤销的分配代数。</param>
    /// <returns>本次撤销生效则为 true。</returns>
    public bool TryRevoke(long assignmentEpoch)
    {
        if (_revoked || assignmentEpoch != _assignmentEpoch)
        {
            return false;
        }

        _revoked = true;
        _observed.Clear();
        _pendingByOffset.Clear();
        return true;
    }

    private bool IsCurrent(long assignmentEpoch) =>
        !_revoked && !_deliveryOrderFaulted && assignmentEpoch == _assignmentEpoch;

    private bool TryGetPending(long offset, long assignmentEpoch, out Pending pending)
    {
        if (IsCurrent(assignmentEpoch)
            && _pendingByOffset.TryGetValue(offset, out var found))
        {
            pending = found;
            return true;
        }

        pending = null!;
        return false;
    }

    private void AdvanceSafeWatermark()
    {
        // 只沿 Consumer 实际交付的队首推进；数字空洞可由压缩或事务记录形成。
        while (_observed.TryPeek(out var first) && first.State == PendingState.Completed)
        {
            _observed.Dequeue();
            _pendingByOffset.Remove(first.Offset);
            _safeNextOffset = checked(first.Offset + 1);
        }
    }

    private sealed class Pending(long offset)
    {
        public long Offset { get; } = offset;

        public PendingState State { get; set; }
    }

    private enum PendingState
    {
        Pending,
        AwaitingIsolation,
        Completed,
    }
}
