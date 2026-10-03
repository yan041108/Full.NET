namespace Full.NET.LogConsumer;

/// <summary>Consumer 实际交付的一条 Kafka 日志记录及来源位点。</summary>
public readonly record struct KafkaLogInput(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    ReadOnlyMemory<byte> Value,
    ReadOnlyMemory<byte> RawKey = default);

/// <summary>单条记录的本轮处理结果；Retry 不得消费后续记录或提交位点。</summary>
public enum LogDeliveryDisposition
{
    /// <summary>下游已确认且来源 Offset 已提交。</summary>
    Completed,

    /// <summary>下游尚未确认，必须原位重试。</summary>
    Retry,
}

/// <summary>受限 DLQ 的低基数隔离原因。</summary>
public enum LogIsolationReason
{
    /// <summary>输入线格式无效。</summary>
    InvalidRecord,

    /// <summary>索引路由不允许或事件已过期。</summary>
    Unrouteable,

    /// <summary>ES 返回确定性的逐项失败。</summary>
    PermanentSinkError,
}

/// <summary>只在 ES 已逐项确认后返回结果的日志写入适配。</summary>
public interface ILogDocumentSink
{
    /// <summary>用固定索引名与事件 ID 幂等写入单条文档。</summary>
    Task<BulkItemOutcome> WriteAsync(
        ParsedLogRecord record,
        string indexName,
        CancellationToken cancellationToken);
}

/// <summary>只在受限 DLQ 的 Broker acks=all 投递报告成功后返回 true。</summary>
public interface ILogDeadLetterSink
{
    /// <summary>持久隔离原始来源记录；失败或确认未知时必须返回 false 或抛出。</summary>
    Task<bool> PublishAsync(
        KafkaLogInput input,
        LogIsolationReason reason,
        CancellationToken cancellationToken);
}

/// <summary>只在 Kafka Broker 确认位点提交后正常返回。</summary>
public interface ILogOffsetCommitter
{
    /// <summary>提交该记录的下一 Offset；失败时抛出，留待重放。</summary>
    void CommitNext(KafkaLogInput input);
}

/// <summary>单消费循环的日志投递协调器；不得并行处理同一 Consumer 的下一记录。</summary>
public sealed class SequentialLogDeliveryProcessor
{
    private readonly ILogDocumentSink _documentSink;
    private readonly ILogDeadLetterSink _deadLetterSink;
    private readonly ILogOffsetCommitter _committer;
    private readonly IReadOnlyDictionary<int, int> _retentionDaysByVersion;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxEventBytes;

    /// <summary>创建顺序处理器，并复制版本策略以防运行中外部修改。</summary>
    public SequentialLogDeliveryProcessor(
        ILogDocumentSink documentSink,
        ILogDeadLetterSink deadLetterSink,
        ILogOffsetCommitter committer,
        IReadOnlyDictionary<int, int> retentionDaysByVersion,
        int maxEventBytes,
        TimeProvider? timeProvider = null)
    {
        _documentSink = documentSink ?? throw new ArgumentNullException(nameof(documentSink));
        _deadLetterSink = deadLetterSink ?? throw new ArgumentNullException(nameof(deadLetterSink));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        ArgumentNullException.ThrowIfNull(retentionDaysByVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEventBytes);
        _retentionDaysByVersion = new Dictionary<int, int>(retentionDaysByVersion);
        _maxEventBytes = maxEventBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>完成 ES 写入或 DLQ 持久确认后才提交下一 Offset；失败留在当前记录。</summary>
    public async Task<LogDeliveryDisposition> ProcessAsync(
        KafkaLogInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Topic) || input.Partition < 0
            || input.Offset is < 0 or long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Kafka 来源位点无效。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var validation = KafkaLogRecordParser.TryParse(
            input.Key, input.Value, _maxEventBytes, out var record);
        if (validation != LogRecordValidationResult.Valid)
        {
            return await IsolateAsync(input, LogIsolationReason.InvalidRecord, cancellationToken);
        }

        if (!record!.TryGetIndexName(_retentionDaysByVersion,
                _timeProvider.GetUtcNow(), out var indexName))
        {
            return await IsolateAsync(input, LogIsolationReason.Unrouteable, cancellationToken);
        }

        var outcome = await _documentSink.WriteAsync(record, indexName!, cancellationToken);
        if (outcome == BulkItemOutcome.Retry)
        {
            return LogDeliveryDisposition.Retry;
        }

        if (outcome == BulkItemOutcome.Isolate)
        {
            return await IsolateAsync(input, LogIsolationReason.PermanentSinkError,
                cancellationToken);
        }

        _committer.CommitNext(input);
        return LogDeliveryDisposition.Completed;
    }

    private async Task<LogDeliveryDisposition> IsolateAsync(
        KafkaLogInput input, LogIsolationReason reason, CancellationToken cancellationToken)
    {
        if (!await _deadLetterSink.PublishAsync(input, reason, cancellationToken))
        {
            return LogDeliveryDisposition.Retry;
        }

        _committer.CommitNext(input);
        return LogDeliveryDisposition.Completed;
    }
}
