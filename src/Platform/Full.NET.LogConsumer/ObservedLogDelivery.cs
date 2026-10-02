namespace Full.NET.LogConsumer;

/// <summary>批量传输仍按逐项回执计数，不把一个 HTTP 成功当作全部成功。</summary>
internal sealed class ObservedLogDocumentBatchSink(ILogDocumentBatchSink inner, ConsumerRuntimeState state) : ILogDocumentBatchSink
{
    public async Task<BulkItemOutcome[]> WriteBatchAsync(IReadOnlyList<LogDocumentWrite> writes, CancellationToken cancellationToken)
    {
        try
        {
            var outcomes = await inner.WriteBatchAsync(writes, cancellationToken);
            foreach (var outcome in outcomes)
                state.Record(outcome switch
                {
                    BulkItemOutcome.Succeeded => ConsumerDeliveryMetric.EsConfirmed,
                    BulkItemOutcome.Retry => ConsumerDeliveryMetric.EsRetry,
                    BulkItemOutcome.Isolate => ConsumerDeliveryMetric.EsIsolated,
                    _ => ConsumerDeliveryMetric.EsError,
                });
            return outcomes;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            state.Record(ConsumerDeliveryMetric.EsError);
            throw;
        }
    }
}

/// <summary>只观察原有 ES 回执，不改变隔离、重试、取消或异常语义。</summary>
internal sealed class ObservedLogDocumentSink(ILogDocumentSink inner, ConsumerRuntimeState state) : ILogDocumentSink
{
    public async Task<BulkItemOutcome> WriteAsync(ParsedLogRecord record, string indexName, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await inner.WriteAsync(record, indexName, cancellationToken);
            state.Record(outcome switch
            {
                BulkItemOutcome.Succeeded => ConsumerDeliveryMetric.EsConfirmed,
                BulkItemOutcome.Retry => ConsumerDeliveryMetric.EsRetry,
                _ => ConsumerDeliveryMetric.EsIsolated,
            });
            return outcome;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            state.Record(ConsumerDeliveryMetric.EsError);
            throw;
        }
    }
}

/// <summary>计数发生在实际 DLQ ACK 之后；失败仍交给原有协调器处理。</summary>
internal sealed class ObservedLogDeadLetterSink(ILogDeadLetterSink inner, ConsumerRuntimeState state) : ILogDeadLetterSink
{
    public async Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason, CancellationToken cancellationToken)
    {
        try
        {
            var confirmed = await inner.PublishAsync(input, reason, cancellationToken);
            state.Record(confirmed ? ConsumerDeliveryMetric.DlqConfirmed : ConsumerDeliveryMetric.DlqRetry);
            return confirmed;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            state.Record(ConsumerDeliveryMetric.DlqError);
            throw;
        }
    }
}

/// <summary>提交成功计数只在同步提交返回后更新，提交异常原样传播。</summary>
internal sealed class ObservedLogOffsetCommitter(ILogOffsetCommitter inner, ConsumerRuntimeState state) : ILogOffsetCommitter
{
    public void CommitNext(KafkaLogInput input)
    {
        try
        {
            inner.CommitNext(input);
            state.Record(ConsumerDeliveryMetric.OffsetConfirmed);
        }
        catch
        {
            state.Record(ConsumerDeliveryMetric.OffsetError);
            throw;
        }
    }
}
