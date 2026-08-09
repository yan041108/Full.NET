using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 在 Kafka Consumer 循环内串行协调分区分配、局部背压、Seek 与 Offset 提交。
/// 本类型不执行 Handler；所有 <see cref="IConsumer{TKey,TValue}"/> 调用都由调用方所在的 Poll 循环触发。
/// </summary>
internal sealed class KafkaConsumerPartitionCoordinator(
    IConsumer<string, byte[]> consumer,
    KafkaPartitionWorkScheduler scheduler,
    KafkaMessagingOptions options,
    string consumerCode,
    ILogger logger)
{
    private const string ProviderCode = "kafka";
    private readonly KafkaPartitionOffsetTracker _offsetTracker = new();
    private readonly Dictionary<TopicPartition, long> _assignmentEpochs = [];
    private readonly HashSet<TopicPartition> _paused = [];
    private readonly Dictionary<TopicPartition, DateTimeOffset> _resumeAtUtc = [];
    private long _nextAssignmentEpoch;

    public void OnAssigned(IEnumerable<TopicPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        foreach (var partition in partitions)
        {
            if (_assignmentEpochs.ContainsKey(partition))
            {
                continue;
            }

            var epoch = checked(++_nextAssignmentEpoch);
            _assignmentEpochs.Add(partition, epoch);
            _offsetTracker.Assign(partition, epoch);
        }
    }

    public void OnRevoked(IEnumerable<TopicPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        foreach (var partition in partitions)
        {
            if (!_assignmentEpochs.Remove(partition, out var epoch))
            {
                continue;
            }

            _offsetTracker.Revoke(partition, epoch);
            _resumeAtUtc.Remove(partition);
            _paused.Remove(partition);
            scheduler.Revoke(partition);
            RecordFlow(partition, "revoked");
        }
    }

    public bool TryDispatch(ConsumeResult<string, byte[]> consumeResult)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        if (!_assignmentEpochs.TryGetValue(
                consumeResult.TopicPartition,
                out var assignmentEpoch))
        {
            return false;
        }

        if (!scheduler.TrySchedule(consumeResult, assignmentEpoch))
        {
            return false;
        }

        _offsetTracker.Track(consumeResult.TopicPartitionOffset, assignmentEpoch);
        if (_paused.Add(consumeResult.TopicPartition))
        {
            consumer.Pause([consumeResult.TopicPartition]);
            RecordFlow(consumeResult.TopicPartition, "paused");
        }

        return true;
    }

    public void ProcessCompletions(DateTimeOffset nowUtc)
    {
        while (scheduler.TryReadCompletion(out var completion))
        {
            var offset = completion.ConsumeResult.TopicPartitionOffset;
            var decision = _offsetTracker.Complete(
                offset,
                completion.AssignmentEpoch,
                completion.ShouldCommit);
            if (decision.IsStale)
            {
                logger.LogDebug(
                    "Ignoring Kafka completion from a revoked assignment for {TopicPartition} at offset {Offset}.",
                    offset.TopicPartition,
                    offset.Offset.Value);
                RecordFlow(offset.TopicPartition, "stale_completion");
                continue;
            }

            if (completion.Exception is not null
                && completion.Exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    completion.Exception,
                    "Kafka partition handler failed unexpectedly for {TopicPartition} at offset {Offset}.",
                    offset.TopicPartition,
                    offset.Offset.Value);
            }

            if (decision.CommitOffset is TopicPartitionOffset commitOffset)
            {
                Commit(commitOffset);
                ResumeIfAssigned(offset.TopicPartition);
                continue;
            }

            if (decision.RetryOffset is not TopicPartitionOffset retryOffset)
            {
                continue;
            }

            if (IsStillAssigned(retryOffset.TopicPartition))
            {
                consumer.Seek(retryOffset);
                _resumeAtUtc[retryOffset.TopicPartition] = nowUtc.AddMilliseconds(
                    options.UncommittedRetryBackoffMilliseconds);
                RecordFlow(retryOffset.TopicPartition, "retry_scheduled");
            }
        }
    }

    public void ResumeDuePartitions(DateTimeOffset nowUtc)
    {
        var due = _resumeAtUtc
            .Where(item => item.Value <= nowUtc)
            .Select(item => item.Key)
            .ToArray();
        foreach (var partition in due)
        {
            _resumeAtUtc.Remove(partition);
            ResumeIfAssigned(partition);
        }
    }

    private void Commit(TopicPartitionOffset commitOffset)
    {
        try
        {
            consumer.Commit([commitOffset]);
            RecordFlow(commitOffset.TopicPartition, "offset_committed");
        }
        catch (KafkaException exception) when (!exception.Error.IsFatal)
        {
            // Inbox 已提交但 Offset 未确认时允许重投；后续更高水位提交或 Inbox 去重均保持至少一次语义。
            logger.LogWarning(
                exception,
                "Kafka offset commit failed for {TopicPartition} at next offset {Offset}; Inbox idempotency protects redelivery.",
                commitOffset.TopicPartition,
                commitOffset.Offset.Value);
            RecordFlow(commitOffset.TopicPartition, "offset_commit_failed");
        }
    }

    private void ResumeIfAssigned(TopicPartition partition)
    {
        if (!_paused.Remove(partition) || !IsStillAssigned(partition))
        {
            return;
        }

        consumer.Resume([partition]);
        RecordFlow(partition, "resumed");
    }

    private bool IsStillAssigned(TopicPartition partition) =>
        _assignmentEpochs.ContainsKey(partition)
        && consumer.Assignment.Contains(partition);

    private void RecordFlow(TopicPartition partition, string result) =>
        KafkaMessagingTelemetry.RecordPartitionFlow(
            ProviderCode,
            KafkaTopicNames.ResolveBaseTopic(partition.Topic),
            consumerCode,
            result);
}
