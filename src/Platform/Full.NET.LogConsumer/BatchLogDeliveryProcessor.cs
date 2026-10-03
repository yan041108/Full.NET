namespace Full.NET.LogConsumer;

/// <summary>有界 Bulk 传输，只返回逐项结果，不拥有来源提交权。</summary>
public interface ILogDocumentBatchSink
{
    /// <summary>按输入顺序写入固定目标并返回同长度的逐项回执。</summary>
    Task<BulkItemOutcome[]> WriteBatchAsync(IReadOnlyList<LogDocumentWrite> writes, CancellationToken cancellationToken);
}

/// <summary>本次批次推进结果；已提交数只包含提交成功的连续前缀记录。</summary>
public readonly record struct BatchLogDeliveryResult(LogDeliveryDisposition Disposition, int CommittedRecords);

/// <summary>单 Poll 循环中的有界批次协调，逐项确认后按分区提交连续前缀。</summary>
public sealed class BatchLogDeliveryProcessor
{
    private readonly ILogDocumentBatchSink _sink;
    private readonly ILogDeadLetterSink _dlq;
    private readonly ILogOffsetCommitter _committer;
    private readonly IReadOnlyDictionary<int, int> _routes;
    private readonly int _maxEventBytes;
    private readonly TimeProvider _clock;
    /// <summary>创建批次协调器，索引保留策略属于调用方已冻结的版本路由。</summary>
    public BatchLogDeliveryProcessor(ILogDocumentBatchSink sink, ILogDeadLetterSink dlq, ILogOffsetCommitter committer,
        IReadOnlyDictionary<int, int> routes, int maxEventBytes, TimeProvider? timeProvider = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _dlq = dlq ?? throw new ArgumentNullException(nameof(dlq));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);
        _routes = new Dictionary<int, int>(routes);
        _maxEventBytes = maxEventBytes;
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>处理至首个未确认项；分配所有权失效时禁止推进。</summary>
    public async Task<BatchLogDeliveryResult> ProcessAsync(IReadOnlyList<KafkaLogInput> inputs, Func<bool> ownsAssignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(ownsAssignment);
        if (inputs.Count is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(inputs));
        cancellationToken.ThrowIfCancellationRequested();
        var batch = inputs.ToArray();
        var lastOffsets = new Dictionary<(string, int), long>();
        foreach (var input in batch)
        {
            if (string.IsNullOrWhiteSpace(input.Topic) || input.Partition < 0 || input.Offset is < 0 or long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(inputs));
            var key = (input.Topic, input.Partition);
            if (lastOffsets.TryGetValue(key, out var previous) && input.Offset <= previous)
                throw new ArgumentException("同分区记录必须按交付顺序递增。", nameof(inputs));
            lastOffsets[key] = input.Offset;
            if (input.Value.Length > _maxEventBytes || input.RawKey.Length > 36)
                throw new ArgumentOutOfRangeException(nameof(inputs), "超限记录须走单条隔离路径。");
        }
        if (!ownsAssignment()) return new(LogDeliveryDisposition.Retry, 0);
        var writes = new List<LogDocumentWrite>(batch.Length);
        var writeIndices = new int[batch.Length];
        var reasons = new LogIsolationReason?[batch.Length];
        for (var i = 0; i < batch.Length; i++)
        {
            writeIndices[i] = -1;
            if (KafkaLogRecordParser.TryParse(batch[i].Key, batch[i].Value, _maxEventBytes, out var record)
                != LogRecordValidationResult.Valid)
                reasons[i] = LogIsolationReason.InvalidRecord;
            else if (!record!.TryGetIndexName(_routes, _clock.GetUtcNow(), out var index))
                reasons[i] = LogIsolationReason.Unrouteable;
            else
            {
                writeIndices[i] = writes.Count;
                writes.Add(new(record, index!));
            }
        }
        var outcomes = writes.Count == 0 ? [] : await _sink.WriteBatchAsync(writes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ownsAssignment() || outcomes.Length != writes.Count
            || outcomes.Any(outcome => outcome is not (BulkItemOutcome.Succeeded or BulkItemOutcome.Retry or BulkItemOutcome.Isolate)))
            return new(LogDeliveryDisposition.Retry, 0);
        var prefix = new Dictionary<(string, int), (KafkaLogInput Last, int Count)>();
        var disposition = LogDeliveryDisposition.Completed;
        for (var i = 0; i < batch.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ownsAssignment()) return new(LogDeliveryDisposition.Retry, 0);
            var reason = reasons[i];
            if (writeIndices[i] >= 0)
            {
                var outcome = outcomes[writeIndices[i]];
                if (outcome == BulkItemOutcome.Retry) { disposition = LogDeliveryDisposition.Retry; break; }
                if (outcome == BulkItemOutcome.Isolate) reason = LogIsolationReason.PermanentSinkError;
            }
            if (reason.HasValue)
            {
                var ack = await _dlq.PublishAsync(batch[i], reason.Value, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ownsAssignment()) return new(LogDeliveryDisposition.Retry, 0);
                if (!ack) { disposition = LogDeliveryDisposition.Retry; break; }
            }
            var key = (batch[i].Topic, batch[i].Partition);
            prefix.TryGetValue(key, out var previous);
            prefix[key] = (batch[i], previous.Count + 1);
        }
        // 后续成功项可以幂等重放；只推进首个未确认项之前的交付前缀。
        var committed = 0;
        foreach (var entry in prefix.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ownsAssignment()) return new(LogDeliveryDisposition.Retry, committed);
            _committer.CommitNext(entry.Last);
            committed += entry.Count;
        }
        return new(disposition, committed);
    }
}
