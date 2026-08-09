using Confluent.Kafka;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 按实际消费顺序维护分区提交水位。Kafka 日志 Offset 可能因压缩或事务记录出现数字空洞，
/// 因此连续性以本 Consumer 已交付序列为准，不能用相邻整数推断。
/// </summary>
internal sealed class KafkaPartitionOffsetTracker
{
    private readonly Dictionary<TopicPartition, PartitionState> _partitions = [];

    public void Assign(TopicPartition topicPartition, long assignmentEpoch) =>
        _partitions[topicPartition] = new PartitionState(assignmentEpoch);

    public void Revoke(TopicPartition topicPartition, long assignmentEpoch)
    {
        if (_partitions.TryGetValue(topicPartition, out var state)
            && state.AssignmentEpoch == assignmentEpoch)
        {
            _partitions.Remove(topicPartition);
        }
    }

    public void Track(TopicPartitionOffset offset, long assignmentEpoch)
    {
        var state = GetCurrentState(offset.TopicPartition, assignmentEpoch);
        if (state.Pending.Count > 0
            && state.Pending[^1].Offset.Offset.Value >= offset.Offset.Value)
        {
            throw new InvalidOperationException(
                $"Kafka offsets for partition '{offset.TopicPartition}' must be tracked in increasing order.");
        }

        state.Pending.Add(new PendingOffset(offset));
    }

    public KafkaPartitionOffsetDecision Complete(
        TopicPartitionOffset offset,
        long assignmentEpoch,
        bool shouldCommit)
    {
        if (!_partitions.TryGetValue(offset.TopicPartition, out var state)
            || state.AssignmentEpoch != assignmentEpoch)
        {
            return KafkaPartitionOffsetDecision.Stale;
        }

        var index = state.Pending.FindIndex(item => item.Offset.Offset == offset.Offset);
        if (index < 0)
        {
            return KafkaPartitionOffsetDecision.Stale;
        }

        if (!shouldCommit)
        {
            state.Pending.RemoveRange(index, state.Pending.Count - index);
            return new KafkaPartitionOffsetDecision(
                null,
                offset,
                IsStale: false);
        }

        state.Pending[index].Completed = true;
        TopicPartitionOffset? commitOffset = null;
        while (state.Pending.Count > 0 && state.Pending[0].Completed)
        {
            var completed = state.Pending[0].Offset;
            state.Pending.RemoveAt(0);
            commitOffset = new TopicPartitionOffset(
                completed.TopicPartition,
                completed.Offset + 1);
        }

        return new KafkaPartitionOffsetDecision(
            commitOffset,
            null,
            IsStale: false);
    }

    private PartitionState GetCurrentState(
        TopicPartition topicPartition,
        long assignmentEpoch)
    {
        if (!_partitions.TryGetValue(topicPartition, out var state)
            || state.AssignmentEpoch != assignmentEpoch)
        {
            throw new InvalidOperationException(
                $"Kafka partition '{topicPartition}' is not owned by assignment epoch {assignmentEpoch}.");
        }

        return state;
    }

    private sealed class PartitionState(long assignmentEpoch)
    {
        public long AssignmentEpoch { get; } = assignmentEpoch;

        public List<PendingOffset> Pending { get; } = [];
    }

    private sealed class PendingOffset(TopicPartitionOffset offset)
    {
        public TopicPartitionOffset Offset { get; } = offset;

        public bool Completed { get; set; }
    }
}

internal readonly record struct KafkaPartitionOffsetDecision(
    TopicPartitionOffset? CommitOffset,
    TopicPartitionOffset? RetryOffset,
    bool IsStale)
{
    public static KafkaPartitionOffsetDecision Stale { get; } =
        new(null, null, IsStale: true);
}
