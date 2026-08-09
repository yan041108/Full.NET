using Confluent.Kafka;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Offset 提交模式；两种模式都只接受上游连续成功水位，不改变至少一次语义。
/// </summary>
public enum KafkaOffsetCommitMode
{
    /// <summary>每次产生连续安全水位后立即提交。</summary>
    PerMessage = 0,

    /// <summary>合并每个分区的最新连续安全水位后周期提交。</summary>
    PeriodicWatermark = 1,
}

/// <summary>
/// 在单一 Consumer Poll 循环内合并安全 Offset；只有 Broker 提交成功后才移除待提交水位。
/// </summary>
internal sealed class KafkaOffsetCommitCoordinator
{
    private readonly KafkaOffsetCommitMode _mode;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;
    private readonly Dictionary<TopicPartition, PendingCommit> _pending = [];
    private DateTimeOffset _nextFlushUtc;
    private DateTimeOffset? _retryNotBeforeUtc;
    private int _pendingOfferCount;

    public KafkaOffsetCommitCoordinator(
        KafkaOffsetCommitMode mode,
        TimeSpan interval,
        int batchSize,
        DateTimeOffset initialUtc)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        _mode = mode;
        _interval = interval;
        _batchSize = batchSize;
        _nextFlushUtc = initialUtc.Add(interval);
    }

    public int PendingPartitionCount => _pending.Count;

    public void Offer(TopicPartitionOffset safeWatermark)
    {
        if (_pending.TryGetValue(safeWatermark.TopicPartition, out var current))
        {
            if (safeWatermark.Offset <= current.Offset.Offset)
            {
                return;
            }

            _pending[safeWatermark.TopicPartition] = current with
            {
                Offset = safeWatermark,
                OfferCount = checked(current.OfferCount + 1),
            };
        }
        else
        {
            _pending.Add(safeWatermark.TopicPartition, new PendingCommit(safeWatermark, 1));
        }

        _pendingOfferCount = checked(_pendingOfferCount + 1);
    }

    public IReadOnlyList<TopicPartitionOffset> GetReady(
        DateTimeOffset nowUtc,
        bool force = false)
    {
        if (_pending.Count == 0
            || (!force && _retryNotBeforeUtc is DateTimeOffset retryUtc && nowUtc < retryUtc)
            || (!force
                && _mode == KafkaOffsetCommitMode.PeriodicWatermark
                && _pendingOfferCount < _batchSize
                && nowUtc < _nextFlushUtc))
        {
            return [];
        }

        return Order(_pending.Values.Select(item => item.Offset));
    }

    public IReadOnlyList<TopicPartitionOffset> GetReadyForPartitions(
        IEnumerable<TopicPartition> partitions,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        var requested = partitions.ToHashSet();
        if (requested.Count == 0)
        {
            return [];
        }

        return Order(
            _pending
                .Where(item => requested.Contains(item.Key))
                .Select(item => item.Value.Offset));
    }

    public void Acknowledge(
        IEnumerable<TopicPartitionOffset> committed,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(committed);
        foreach (var offset in committed)
        {
            if (!_pending.TryGetValue(offset.TopicPartition, out var current)
                || current.Offset.Offset > offset.Offset)
            {
                continue;
            }

            _pending.Remove(offset.TopicPartition);
            _pendingOfferCount -= current.OfferCount;
        }

        _nextFlushUtc = nowUtc.Add(_interval);
        _retryNotBeforeUtc = null;
    }

    public void RecordFailure(DateTimeOffset nowUtc)
    {
        _nextFlushUtc = nowUtc.Add(_interval);
        _retryNotBeforeUtc = _nextFlushUtc;
    }

    public void Discard(IEnumerable<TopicPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        foreach (var partition in partitions)
        {
            if (_pending.Remove(partition, out var removed))
            {
                _pendingOfferCount -= removed.OfferCount;
            }
        }
    }

    private static IReadOnlyList<TopicPartitionOffset> Order(
        IEnumerable<TopicPartitionOffset> offsets) =>
        offsets
            .OrderBy(offset => offset.Topic, StringComparer.Ordinal)
            .ThenBy(offset => offset.Partition.Value)
            .ToArray();

    private sealed record PendingCommit(
        TopicPartitionOffset Offset,
        int OfferCount);
}
