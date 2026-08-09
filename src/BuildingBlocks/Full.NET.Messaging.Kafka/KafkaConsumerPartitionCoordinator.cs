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
    ILogger logger,
    DateTimeOffset? initialUtc = null)
{
    private const string ProviderCode = "kafka";
    private readonly KafkaPartitionOffsetTracker _offsetTracker = new();
    private readonly Dictionary<TopicPartition, long> _assignmentEpochs = [];
    private readonly HashSet<TopicPartition> _paused = [];
    private readonly HashSet<TopicPartition> _partitionPressurePaused = [];
    private readonly Dictionary<TopicPartition, DateTimeOffset> _resumeAtUtc = [];
    private readonly KafkaOffsetCommitCoordinator _commitCoordinator = new(
        options.OffsetCommitMode,
        TimeSpan.FromMilliseconds(options.OffsetCommitIntervalMilliseconds),
        options.OffsetCommitBatchSize,
        initialUtc ?? DateTimeOffset.UtcNow);
    private long _nextAssignmentEpoch;
    private bool _globalPressurePaused;

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

        ApplyPauseState();
        RecordState();
    }

    public void OnRevoked(IEnumerable<TopicPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        var revoked = partitions.Distinct().ToArray();
        FlushOffsets(
            _commitCoordinator.GetReadyForPartitions(revoked, DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);
        // Revoke 回调结束后本实例不再拥有提交权；即使最后一次 Commit 非致命失败，
        // 也必须丢弃本地待提交水位，让新 Owner 重投并由 Inbox 去重。
        _commitCoordinator.Discard(revoked);
        RemoveAssignments(revoked, "revoked");
    }

    /// <summary>
    /// 丢失分区时 Broker 已不再保证当前实例拥有提交权，因此只能丢弃本地待提交水位。
    /// </summary>
    public void OnLost(IEnumerable<TopicPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        var lost = partitions.Distinct().ToArray();
        _commitCoordinator.Discard(lost);
        RemoveAssignments(lost, "lost");
    }

    private void RemoveAssignments(
        IEnumerable<TopicPartition> partitions,
        string flowResult)
    {
        foreach (var partition in partitions)
        {
            if (!_assignmentEpochs.Remove(partition, out var epoch))
            {
                continue;
            }

            _offsetTracker.Revoke(partition, epoch);
            _resumeAtUtc.Remove(partition);
            _partitionPressurePaused.Remove(partition);
            _paused.Remove(partition);
            scheduler.Revoke(partition);
            RecordFlow(partition, flowResult);
        }

        RecordState();
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

        if (!scheduler.TrySchedule(consumeResult, assignmentEpoch, out var pressure))
        {
            return false;
        }

        _offsetTracker.Track(consumeResult.TopicPartitionOffset, assignmentEpoch);
        if (pressure.ReachedPartitionHighWatermark)
        {
            _partitionPressurePaused.Add(consumeResult.TopicPartition);
        }


        if (pressure.ReachedGlobalHighWatermark)
        {
            _globalPressurePaused = true;
        }

        ApplyPauseState();
        RecordState();

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
                _commitCoordinator.Offer(commitOffset);
                continue;
            }

            if (decision.RetryOffset is not TopicPartitionOffset retryOffset)
            {
                continue;
            }

            if (IsStillAssigned(retryOffset.TopicPartition))
            {
                scheduler.Revoke(retryOffset.TopicPartition);
                RestartAssignmentEpoch(retryOffset.TopicPartition);
                _partitionPressurePaused.Remove(retryOffset.TopicPartition);
                consumer.Seek(retryOffset);
                _resumeAtUtc[retryOffset.TopicPartition] = nowUtc.AddMilliseconds(
                    options.UncommittedRetryBackoffMilliseconds);
                RecordFlow(retryOffset.TopicPartition, "retry_scheduled");
            }
        }

        FlushOffsets(_commitCoordinator.GetReady(nowUtc), nowUtc);
        EvaluatePressureAndResume();
        RecordState();
    }

    private void RestartAssignmentEpoch(TopicPartition partition)
    {
        if (!_assignmentEpochs.TryGetValue(partition, out var previousEpoch))
        {
            return;
        }

        // Seek 后同一 Offset 会重新进入新 Lane；提升 epoch 可阻止旧 Lane 迟到完成
        // 与新投递使用相同 Offset 时发生 ABA 混淆。
        _offsetTracker.Revoke(partition, previousEpoch);
        var nextEpoch = checked(++_nextAssignmentEpoch);
        _assignmentEpochs[partition] = nextEpoch;
        _offsetTracker.Assign(partition, nextEpoch);
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
        }

        EvaluatePressureAndResume();
        RecordState();
    }

    private void FlushOffsets(
        IReadOnlyList<TopicPartitionOffset> commitOffsets,
        DateTimeOffset nowUtc)
    {
        if (commitOffsets.Count == 0)
        {
            return;
        }

        try
        {
            using var activity = KafkaMessagingTelemetry.StartCommitActivity(
                consumerCode,
                commitOffsets.Count);
            consumer.Commit(commitOffsets);
            _commitCoordinator.Acknowledge(commitOffsets, nowUtc);
            foreach (var commitOffset in commitOffsets)
            {
                RecordFlow(commitOffset.TopicPartition, "offset_committed");
            }
        }
        catch (KafkaException exception) when (!exception.Error.IsFatal)
        {
            // Inbox 已提交但 Offset 未确认时允许重投；后续更高水位提交或 Inbox 去重均保持至少一次语义。
            logger.LogWarning(
                exception,
                "Kafka offset commit failed for {PartitionCount} partitions; pending safe watermarks will be retried and Inbox idempotency protects redelivery.",
                commitOffsets.Count);
            _commitCoordinator.RecordFailure(nowUtc);
            foreach (var commitOffset in commitOffsets)
            {
                RecordFlow(commitOffset.TopicPartition, "offset_commit_failed");
            }
        }
    }

    private void EvaluatePressureAndResume()
    {
        foreach (var partition in _partitionPressurePaused.ToArray())
        {
            if (scheduler.ShouldResumePartition(partition))
            {
                _partitionPressurePaused.Remove(partition);
            }
        }

        if (_globalPressurePaused && scheduler.ShouldResumeGlobally)
        {
            _globalPressurePaused = false;
        }

        foreach (var partition in _paused.ToArray())
        {
            if (_globalPressurePaused
                || _partitionPressurePaused.Contains(partition)
                || _resumeAtUtc.ContainsKey(partition)
                || !IsStillAssigned(partition))
            {
                continue;
            }

            _paused.Remove(partition);
            consumer.Resume([partition]);
            RecordFlow(partition, "resumed");
        }
    }

    private void ApplyPauseState()
    {
        IEnumerable<TopicPartition> candidates = _globalPressurePaused
            ? _assignmentEpochs.Keys
            : _partitionPressurePaused;
        var targets = candidates
            .Where(partition => !_paused.Contains(partition) && IsStillAssigned(partition))
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        consumer.Pause(targets);
        foreach (var partition in targets)
        {
            _paused.Add(partition);
            RecordFlow(partition, "paused");
        }
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

    private void RecordState() =>
        KafkaMessagingTelemetry.UpdateConsumerState(
            ProviderCode,
            consumerCode,
            scheduler.ActiveHandlerCount,
            scheduler.BufferDepth,
            _assignmentEpochs.Count,
            _paused.Count);
}
