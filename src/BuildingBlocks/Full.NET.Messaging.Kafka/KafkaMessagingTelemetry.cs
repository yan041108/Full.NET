using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Provider 与 CDC 平台侧低基数指标；禁止 MessageId、TenantId、原始 Topic、Payload、Secret、SQL 或异常文本标签。
/// </summary>
public static class KafkaMessagingTelemetry
{
    private const int MaximumConsumerStates = 1_024;
    private const int MaximumConnectorStates = 256;
    public const string MeterName = "Full.NET.Messaging";
    public const string ActivitySourceName = "Full.NET.Messaging.Kafka";

    /// <summary>允许出现在本 Meter 标签键中的白名单；用于契约测试。</summary>
    public static readonly IReadOnlyList<string> AllowedTagKeys =
    [
        "provider",
        "database_provider",
        "topic_code",
        "consumer_code",
        "message_type_code",
        "result",
        "reason_code",
        "connector_code",
    ];

    /// <summary>明确禁止进入指标标签的键名片段（大小写不敏感匹配）。</summary>
    public static readonly IReadOnlyList<string> ForbiddenTagKeyFragments =
    [
        "secret",
        "payload",
        "sql",
        "tenant",
        "user",
        "password",
        "token",
        "connection",
    ];

    private static readonly Meter Meter = new(MeterName);
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly ConcurrentDictionary<string, ConsumerState> ConsumerStates =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ConnectorState> ConnectorStates =
        new(StringComparer.Ordinal);
    private static long _compatibilityProcessingSequence;
    private static long _sqlServerCaptureJobRunning = 1;
    private static double _mySqlBinlogRetentionHours = 168d;

    private static readonly Counter<long> ConsumeResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.consume.results");
    private static readonly Counter<long> PartitionFlowResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.partition.flow.results");
    private static readonly Counter<long> InboxDuplicates =
        Meter.CreateCounter<long>("fullnet.messaging.inbox.duplicates");
    private static readonly Counter<long> RetryRouted =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.retry.routed");
    private static readonly Counter<long> DeadLetterPublished =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.dead_letter.published");
    private static readonly Counter<long> UncommittedRetries =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.uncommitted.retry");
    private static readonly Counter<long> OwnershipTransitions =
        Meter.CreateCounter<long>("fullnet.messaging.ownership.transitions");
    private static readonly Histogram<double> OwnershipWait =
        Meter.CreateHistogram<double>("fullnet.messaging.ownership.wait", unit: "s");
    private static readonly Counter<long> ConnectorErrors =
        Meter.CreateCounter<long>("fullnet.messaging.connector.errors");

    private static readonly ObservableGauge<long> Inflight = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.inflight",
        () => Observe(state => state.Inflight));
    private static readonly ObservableGauge<long> BufferDepth = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.buffer.depth",
        () => Observe(state => state.BufferDepth));
    private static readonly ObservableGauge<long> AssignedPartitions = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.assigned.partitions",
        () => Observe(state => state.AssignedPartitions));
    private static readonly ObservableGauge<long> PausedPartitions = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.paused.partitions",
        () => Observe(state => state.PausedPartitions));
    private static readonly ObservableGauge<long> OwnershipRevoked = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.ownership.revoked",
        () => Observe(state => state.OwnershipRevoked ? 1 : 0));
    private static readonly ObservableGauge<long> ConsumerLag = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.consumer.lag",
        () => Observe(state => state.ConsumerLag),
        unit: "{message}");
    private static readonly ObservableGauge<double> LagRetentionRatio = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.lag_retention_ratio",
        () => ObserveRatio(state => state.LagRetentionRatio),
        unit: "1");
    private static readonly ObservableGauge<double> ConnectorLag = Meter.CreateObservableGauge(
        "fullnet.messaging.connector.lag",
        () => ObserveConnectorLag(),
        unit: "s");
    private static readonly ObservableGauge<long> ConnectorOffsetUnrecoverable =
        Meter.CreateObservableGauge(
            "fullnet.messaging.connector.offset.unrecoverable",
            () => ObserveConnectorUnrecoverable());
    private static readonly ObservableGauge<long> SqlServerCaptureJobRunning =
        Meter.CreateObservableGauge(
            "fullnet.messaging.cdc.sqlserver.capture_job_running",
            ObserveSqlServerCaptureJobRunning);
    private static readonly ObservableGauge<double> MySqlBinlogRetentionHours =
        Meter.CreateObservableGauge(
            "fullnet.messaging.cdc.mysql.binlog_retention_hours",
            ObserveMySqlBinlogRetentionHours,
            unit: "h");

    public static Activity? StartConsumeActivity(
        string topicCode,
        string consumerCode,
        int partition,
        long offset,
        string? traceParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        Activity? activity;
        if (!string.IsNullOrWhiteSpace(traceParent)
            && ActivityContext.TryParse(traceParent, null, true, out var parentContext))
        {
            activity = ActivitySource.StartActivity(
                "fullnet.messaging.kafka.consume",
                ActivityKind.Consumer,
                parentContext);
        }
        else
        {
            activity = ActivitySource.StartActivity(
                "fullnet.messaging.kafka.consume",
                ActivityKind.Consumer);
        }

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topicCode);
        activity?.SetTag("messaging.consumer.group.name", consumerCode);
        activity?.SetTag("messaging.kafka.partition", partition);
        activity?.SetTag("messaging.kafka.message.offset", offset);
        return activity;
    }

    public static Activity? StartCommitActivity(
        string consumerCode,
        int partitionCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (partitionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        }

        var activity = ActivitySource.StartActivity(
            "fullnet.messaging.kafka.commit",
            ActivityKind.Client);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.consumer.group.name", consumerCode);
        activity?.SetTag("messaging.kafka.commit.partition_count", partitionCount);
        return activity;
    }

    public static void UpdateConsumerState(
        string provider,
        string consumerCode,
        int inflight,
        int bufferDepth,
        int assignedPartitions,
        int pausedPartitions,
        bool? ownershipRevoked = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (inflight < 0
            || bufferDepth < 0
            || assignedPartitions < 0
            || pausedPartitions < 0
            || pausedPartitions > assignedPartitions)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferDepth));
        }

        try
        {
            if (!ConsumerStates.ContainsKey(consumerCode)
                && ConsumerStates.Count >= MaximumConsumerStates)
            {
                return;
            }

            ConsumerStates.AddOrUpdate(
                consumerCode,
                _ => new ConsumerState(
                    provider,
                    consumerCode,
                    inflight,
                    bufferDepth,
                    assignedPartitions,
                    pausedPartitions,
                    ownershipRevoked ?? false,
                    ProcessingSequence: 0,
                    ConsumerLag: 0,
                    LagRetentionRatio: 0d),
                (_, current) => current with
                {
                    Provider = provider,
                    AssignedPartitions = assignedPartitions,
                    PausedPartitions = pausedPartitions,
                    OwnershipRevoked = ownershipRevoked ?? current.OwnershipRevoked,
                });
        }
        catch (Exception)
        {
            // 状态遥测旁路失败不得影响消费、提交或 Rebalance。
        }
    }

    /// <summary>
    /// 更新 Consumer Group 相对高水位的消息滞后与相对保留窗口占比（0～1+）。
    /// </summary>
    public static void UpdateConsumerLag(
        string provider,
        string consumerCode,
        long lagMessages,
        double lagRetentionRatio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (lagMessages < 0
            || lagRetentionRatio < 0d
            || double.IsNaN(lagRetentionRatio)
            || double.IsInfinity(lagRetentionRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(lagMessages));
        }

        try
        {
            if (!ConsumerStates.ContainsKey(consumerCode)
                && ConsumerStates.Count >= MaximumConsumerStates)
            {
                return;
            }

            ConsumerStates.AddOrUpdate(
                consumerCode,
                _ => new ConsumerState(
                    provider,
                    consumerCode,
                    Inflight: 0,
                    BufferDepth: 0,
                    AssignedPartitions: 0,
                    PausedPartitions: 0,
                    OwnershipRevoked: false,
                    ProcessingSequence: 0,
                    ConsumerLag: lagMessages,
                    LagRetentionRatio: lagRetentionRatio),
                (_, current) => current with
                {
                    Provider = provider,
                    ConsumerLag = lagMessages,
                    LagRetentionRatio = lagRetentionRatio,
                });
        }
        catch (Exception)
        {
            // lag 采样失败不得阻断排空或回退证明。
        }
    }

    public static void SetOwnershipRevoked(string consumerCode, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        try
        {
            if (!ConsumerStates.ContainsKey(consumerCode))
            {
                // 状态尚未建立时仍发出转换事件，避免所有权 Fence 发生在首次 RecordState 之前漏计。
                RecordOwnershipTransition(
                    "kafka",
                    consumerCode,
                    value ? "revoked" : "restored");
                return;
            }

            while (ConsumerStates.TryGetValue(consumerCode, out var current))
            {
                if (current.OwnershipRevoked == value)
                {
                    return;
                }

                if (ConsumerStates.TryUpdate(
                        consumerCode,
                        current with { OwnershipRevoked = value },
                        current))
                {
                    RecordOwnershipTransition(
                        current.Provider,
                        consumerCode,
                        value ? "revoked" : "restored");
                    return;
                }
            }
        }
        catch (Exception)
        {
            // 所有权遥测失败不得改变 Fence 行为。
        }
    }

    public static void UpdateProcessingState(
        string provider,
        string consumerCode,
        int inflight,
        int bufferDepth) =>
        UpdateProcessingState(
            provider,
            consumerCode,
            Interlocked.Increment(ref _compatibilityProcessingSequence),
            inflight,
            bufferDepth);

    internal static void UpdateProcessingState(
        string provider,
        string consumerCode,
        long sequence,
        int inflight,
        int bufferDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (sequence < 1 || inflight < 0 || bufferDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferDepth));
        }

        try
        {
            while (ConsumerStates.TryGetValue(consumerCode, out var current))
            {
                if (sequence <= current.ProcessingSequence)
                {
                    return;
                }

                if (ConsumerStates.TryUpdate(
                        consumerCode,
                        current with
                        {
                            Provider = provider,
                            Inflight = inflight,
                            BufferDepth = bufferDepth,
                            ProcessingSequence = sequence,
                        },
                        current))
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Handler 热路径状态采集失败不得影响消息处理。
        }
    }

    public static void RemoveConsumerState(string consumerCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        try
        {
            ConsumerStates.TryRemove(consumerCode, out _);
        }
        catch (Exception)
        {
            // 清理遥测状态失败不得阻塞 Worker 退出。
        }
    }

    public static void RecordConsume(
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string result,
        string? reasonCode = null)
    {
        Record(
            ConsumeResults,
            provider,
            topicCode,
            consumerCode,
            messageTypeCode,
            result,
            reasonCode);

        if (string.Equals(result, "already_processed", StringComparison.Ordinal))
        {
            RecordInboxDuplicate(provider, topicCode, consumerCode, messageTypeCode);
        }
        else if (string.Equals(result, "retry_routed", StringComparison.Ordinal))
        {
            RecordRetryRouted(provider, topicCode, consumerCode, messageTypeCode, reasonCode);
        }
        else if (string.Equals(result, "dead_lettered", StringComparison.Ordinal))
        {
            RecordDeadLetter(provider, topicCode, consumerCode, messageTypeCode, reasonCode);
        }
    }

    public static void RecordPartitionFlow(
        string provider,
        string topicCode,
        string consumerCode,
        string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        try
        {
            PartitionFlowResults.Add(
                1,
                new TagList
                {
                    { "provider", provider },
                    { "topic_code", topicCode },
                    { "consumer_code", consumerCode },
                    { "result", result },
                });

            if (string.Equals(result, "retry_scheduled", StringComparison.Ordinal))
            {
                UncommittedRetries.Add(
                    1,
                    new TagList
                    {
                        { "provider", provider },
                        { "topic_code", topicCode },
                        { "consumer_code", consumerCode },
                    });
            }
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响分区背压与 Offset 语义。
        }
    }

    /// <summary>记录 Inbox 幂等命中（重复投递且业务副作用为零）。</summary>
    public static void RecordInboxDuplicate(
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode) =>
        RecordSimple(
            InboxDuplicates,
            provider,
            topicCode,
            consumerCode,
            messageTypeCode);

    /// <summary>记录成功路由到静态 Retry Topic。</summary>
    public static void RecordRetryRouted(
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string? reasonCode = null) =>
        Record(
            RetryRouted,
            provider,
            topicCode,
            consumerCode,
            messageTypeCode,
            result: "retry_routed",
            reasonCode);

    /// <summary>记录成功发布到 DLQ Topic。</summary>
    public static void RecordDeadLetter(
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string? reasonCode = null) =>
        Record(
            DeadLetterPublished,
            provider,
            topicCode,
            consumerCode,
            messageTypeCode,
            result: "dead_lettered",
            reasonCode);

    /// <summary>
    /// 记录所有权切换结果；<paramref name="result"/> 仅允许稳定机器码，例如
    /// <c>revoked</c>、<c>restored</c>、<c>cutover</c>、<c>rollback</c>。
    /// </summary>
    public static void RecordOwnershipTransition(
        string provider,
        string consumerCode,
        string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        try
        {
            OwnershipTransitions.Add(
                1,
                new TagList
                {
                    { "provider", provider },
                    { "consumer_code", consumerCode },
                    { "result", result },
                });
        }
        catch (Exception)
        {
            // 所有权转换计数失败不得影响 Fence 或切流事务。
        }
    }

    /// <summary>记录因所有权撤销而主动退避等待的时长。</summary>
    public static void RecordOwnershipWait(
        string provider,
        string consumerCode,
        double waitSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (waitSeconds < 0d || double.IsNaN(waitSeconds) || double.IsInfinity(waitSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(waitSeconds));
        }

        try
        {
            OwnershipWait.Record(
                waitSeconds,
                new TagList
                {
                    { "provider", provider },
                    { "consumer_code", consumerCode },
                });
        }
        catch (Exception)
        {
            // 等待直方图失败不得缩短或拉长所有权退避。
        }
    }

    /// <summary>
    /// 更新或写入 Connector 滞后与位点不可恢复占位状态；生产采集器可定期调用。
    /// </summary>
    public static void UpdateConnectorHealth(
        string provider,
        string connectorCode,
        double lagSeconds,
        bool offsetUnrecoverable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorCode);
        if (lagSeconds < 0d || double.IsNaN(lagSeconds) || double.IsInfinity(lagSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(lagSeconds));
        }

        try
        {
            if (!ConnectorStates.ContainsKey(connectorCode)
                && ConnectorStates.Count >= MaximumConnectorStates)
            {
                return;
            }

            ConnectorStates[connectorCode] = new ConnectorState(
                provider,
                connectorCode,
                lagSeconds,
                offsetUnrecoverable);
        }
        catch (Exception)
        {
            // Connector 占位指标失败不得影响 Connect REST 控制面。
        }
    }

    /// <summary>累计 Connector 错误次数；原因码必须稳定且不得包含异常文本。</summary>
    public static void RecordConnectorError(
        string provider,
        string connectorCode,
        string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        try
        {
            ConnectorErrors.Add(
                1,
                new TagList
                {
                    { "provider", provider },
                    { "connector_code", connectorCode },
                    { "reason_code", reasonCode },
                });
        }
        catch (Exception)
        {
            // Connector 错误计数失败不得改变连接器启停语义。
        }
    }

    /// <summary>
    /// 写入 SQL Server CDC Capture Job 与 MySQL Binlog 保留窗口占位值。
    /// 正式环境应由平台采集器填充；缺省 Job=运行中、Binlog=7 天，避免误报阻断开发。
    /// </summary>
    public static void UpdateCdcPlatformHealth(
        bool sqlServerCaptureJobRunning,
        double mySqlBinlogRetentionHours)
    {
        if (mySqlBinlogRetentionHours < 0d
            || double.IsNaN(mySqlBinlogRetentionHours)
            || double.IsInfinity(mySqlBinlogRetentionHours))
        {
            throw new ArgumentOutOfRangeException(nameof(mySqlBinlogRetentionHours));
        }

        Volatile.Write(
            ref _sqlServerCaptureJobRunning,
            sqlServerCaptureJobRunning ? 1L : 0L);
        Volatile.Write(ref _mySqlBinlogRetentionHours, mySqlBinlogRetentionHours);
    }

    private static void RecordSimple(
        Counter<long> counter,
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeCode);

        try
        {
            counter.Add(
                1,
                new TagList
                {
                    { "provider", provider },
                    { "topic_code", topicCode },
                    { "consumer_code", consumerCode },
                    { "message_type_code", messageTypeCode },
                });
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响消费语义。
        }
    }

    private static void Record(
        Counter<long> counter,
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string result,
        string? reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        try
        {
            var tags = new TagList
            {
                { "provider", provider },
                { "topic_code", topicCode },
                { "consumer_code", consumerCode },
                { "message_type_code", messageTypeCode },
                { "result", result },
            };

            if (!string.IsNullOrWhiteSpace(reasonCode))
            {
                tags.Add("reason_code", reasonCode);
            }

            counter.Add(1, tags);
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响消费语义。
        }
    }

    private static IEnumerable<Measurement<long>> ObserveSqlServerCaptureJobRunning()
    {
        yield return new Measurement<long>(
            Volatile.Read(ref _sqlServerCaptureJobRunning));
    }

    private static IEnumerable<Measurement<double>> ObserveMySqlBinlogRetentionHours()
    {
        yield return new Measurement<double>(
            Volatile.Read(ref _mySqlBinlogRetentionHours));
    }

    private static IEnumerable<Measurement<long>> Observe(
        Func<ConsumerState, long> valueSelector)
    {
        foreach (var state in ConsumerStates.Values)
        {
            yield return new Measurement<long>(
                valueSelector(state),
                new KeyValuePair<string, object?>("provider", state.Provider),
                new KeyValuePair<string, object?>("consumer_code", state.ConsumerCode));
        }
    }

    private static IEnumerable<Measurement<double>> ObserveRatio(
        Func<ConsumerState, double> valueSelector)
    {
        foreach (var state in ConsumerStates.Values)
        {
            yield return new Measurement<double>(
                valueSelector(state),
                new KeyValuePair<string, object?>("provider", state.Provider),
                new KeyValuePair<string, object?>("consumer_code", state.ConsumerCode));
        }
    }

    private static IEnumerable<Measurement<double>> ObserveConnectorLag()
    {
        foreach (var state in ConnectorStates.Values)
        {
            yield return new Measurement<double>(
                state.LagSeconds,
                new KeyValuePair<string, object?>("provider", state.Provider),
                new KeyValuePair<string, object?>("connector_code", state.ConnectorCode));
        }
    }

    private static IEnumerable<Measurement<long>> ObserveConnectorUnrecoverable()
    {
        foreach (var state in ConnectorStates.Values)
        {
            yield return new Measurement<long>(
                state.OffsetUnrecoverable ? 1 : 0,
                new KeyValuePair<string, object?>("provider", state.Provider),
                new KeyValuePair<string, object?>("connector_code", state.ConnectorCode));
        }
    }

    private sealed record ConsumerState(
        string Provider,
        string ConsumerCode,
        int Inflight,
        int BufferDepth,
        int AssignedPartitions,
        int PausedPartitions,
        bool OwnershipRevoked,
        long ProcessingSequence,
        long ConsumerLag,
        double LagRetentionRatio);

    private sealed record ConnectorState(
        string Provider,
        string ConnectorCode,
        double LagSeconds,
        bool OffsetUnrecoverable);
}
