namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 一次性 Kafka 范围重放请求；时间范围与 Offset 范围必须二选一，且不修改正式 Consumer Group 水位。
/// 重放使用临时唯一 Consumer Group 进行显式 Assign，仅驱动 Inbox 幂等表与业务 Handler，
/// 绝不影响线上 Consumer Group 的 Committed Offset。
/// </summary>
public sealed class KafkaReplayRequest
{
    /// <summary>
    /// 单次重放允许选择的最大分区数；过多分区会在 API 同步调用窗口内耗尽线程池资源。
    /// </summary>
    public const int MaximumPartitions = 32;

    /// <summary>
    /// 校验并构造重放请求；非法参数抛出带稳定原因码的异常。
    /// </summary>
    /// <param name="topicCode">目录注册的逻辑 Topic 代码。</param>
    /// <param name="fromTimestampUtc">UTC 起始时间戳（含）；必须与 <paramref name="toTimestampUtc"/> 成对提供。</param>
    /// <param name="toTimestampUtc">UTC 结束时间戳（含）；必须与 <paramref name="fromTimestampUtc"/> 成对提供。</param>
    /// <param name="fromOffset">起始 Offset（含）；必须与 <paramref name="toOffset"/> 成对提供，且不得与时间范围同时提供。</param>
    /// <param name="toOffset">结束 Offset（含）；必须与 <paramref name="fromOffset"/> 成对提供。</param>
    /// <param name="partitions">需要重放的分区列表；空数组表示全部分区，但总数不得超过 <see cref="MaximumPartitions"/>。</param>
    /// <param name="replayConsumerName">发起重放的业务消费者名；用于审计与临时 Group 后缀拼接。</param>
    /// <param name="maxMessages">单分区最多扫描的消息数；上限 100,000 防止调用方误操作。</param>
    /// <param name="reason">审计原因文本，1-512 字符。</param>
    public KafkaReplayRequest(
        string topicCode,
        DateTimeOffset? fromTimestampUtc,
        DateTimeOffset? toTimestampUtc,
        long? fromOffset,
        long? toOffset,
        IReadOnlyList<int> partitions,
        string replayConsumerName,
        int maxMessages,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(topicCode)
            || !MessagingNames.TopicCodePattern.IsMatch(topicCode))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.TopicCodeInvalid,
                nameof(topicCode));
        }

        ArgumentNullException.ThrowIfNull(partitions);
        if (partitions.Count > MaximumPartitions
            || partitions.Any(partition => partition < 0)
            || partitions.Distinct().Count() != partitions.Count)
        {
            throw new ArgumentException(
                $"Partitions must contain at most {MaximumPartitions} unique non-negative values.",
                nameof(partitions));
        }

        if (string.IsNullOrWhiteSpace(replayConsumerName)
            || replayConsumerName.Length > MessagingNames.ConsumerNameMaxLength
            || !MessagingNames.ConsumerNamePattern.IsMatch(replayConsumerName))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ConsumerNameInvalid,
                nameof(replayConsumerName));
        }

        if (maxMessages is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
        {
            throw new ArgumentException(
                "A replay audit reason between 1 and 512 characters is required.",
                nameof(reason));
        }

        var hasAnyTimestamp = fromTimestampUtc.HasValue || toTimestampUtc.HasValue;
        var hasCompleteTimestamp = fromTimestampUtc.HasValue && toTimestampUtc.HasValue;
        var hasAnyOffset = fromOffset.HasValue || toOffset.HasValue;
        var hasCompleteOffset = fromOffset.HasValue && toOffset.HasValue;
        if (hasAnyTimestamp != hasCompleteTimestamp
            || hasAnyOffset != hasCompleteOffset
            || hasCompleteTimestamp == hasCompleteOffset)
        {
            throw new ArgumentException(
                "Exactly one complete timestamp or offset range must be supplied.");
        }

        if (hasCompleteTimestamp
            && (fromTimestampUtc!.Value.Offset != TimeSpan.Zero
                || toTimestampUtc!.Value.Offset != TimeSpan.Zero
                || fromTimestampUtc > toTimestampUtc))
        {
            throw new ArgumentException(
                "Replay timestamps must be UTC and ordered from earliest to latest.");
        }

        if (hasCompleteOffset
            && (fromOffset < 0
                || toOffset < 0
                || toOffset == long.MaxValue
                || fromOffset > toOffset))
        {
            throw new ArgumentException(
                "Replay offsets must be non-negative and ordered from lowest to highest.");
        }

        TopicCode = topicCode;
        FromTimestampUtc = fromTimestampUtc;
        ToTimestampUtc = toTimestampUtc;
        FromOffset = fromOffset;
        ToOffset = toOffset;
        Partitions = partitions.ToArray();
        ReplayConsumerName = replayConsumerName;
        MaxMessages = maxMessages;
        Reason = reason.Trim();
    }

    /// <summary>
    /// 目标逻辑 Topic 代码。
    /// </summary>
    public string TopicCode { get; }

    /// <summary>
    /// UTC 起始时间戳（含）；采用时间范围重放时不为 null。
    /// </summary>
    public DateTimeOffset? FromTimestampUtc { get; }

    /// <summary>
    /// UTC 结束时间戳（含）；采用时间范围重放时不为 null。
    /// </summary>
    public DateTimeOffset? ToTimestampUtc { get; }

    /// <summary>
    /// 起始 Offset（含）；采用 Offset 范围重放时不为 null。
    /// </summary>
    public long? FromOffset { get; }

    /// <summary>
    /// 结束 Offset（含）；采用 Offset 范围重放时不为 null。
    /// </summary>
    public long? ToOffset { get; }

    /// <summary>
    /// 指定重放的分区列表；空数组代表 Broker 上该 Topic 的全部分区。
    /// </summary>
    public IReadOnlyList<int> Partitions { get; }

    /// <summary>
    /// 逻辑消费者名，用于 Inbox 幂等判定、审计日志与临时 Consumer Group 命名后缀。
    /// </summary>
    public string ReplayConsumerName { get; }

    /// <summary>
    /// 单分区最多扫描的消息条数上限；达到后立即停止该分区继续读取。
    /// </summary>
    public int MaxMessages { get; }

    /// <summary>
    /// 重放操作的审计原因文本；必须包含操作人、业务单号或故障单号等可追溯信息。
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 本次请求是否使用时间范围定位。
    /// </summary>
    public bool UsesTimeRange => FromTimestampUtc.HasValue;

    /// <summary>
    /// 本次请求是否使用精确 Offset 范围定位。
    /// </summary>
    public bool UsesOffsetRange => FromOffset.HasValue;
}

/// <summary>
/// Kafka 范围重放服务契约；实现需保证：
/// 1. 使用临时独立 Consumer Group，绝不修改正式消费组的 Committed Offset；
/// 2. 每分区到达高水位或 <see cref="KafkaReplayRequest.MaxMessages"/> 后立即退出；
/// 3. 严格复用 Inbox 幂等表判定 <c>AlreadyProcessed</c>，避免重复执行业务副作用。
/// </summary>
public interface IKafkaReplayService
{
    /// <summary>
    /// 按请求参数执行一次同步范围重放，并返回汇总结果。
    /// </summary>
    /// <param name="request">范围重放请求契约。</param>
    /// <param name="cancellationToken">取消令牌；取消时返回已扫描部分的进度而非抛错。</param>
    Task<KafkaReplayResult> ReplayAsync(
        KafkaReplayRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// API 一次性重放的运行门禁；默认关闭，避免把长时间 Broker 消费绑定到普通 HTTP 请求。
/// </summary>
public sealed record KafkaReplayExecutionPolicy(
    /// <summary>
    /// 是否允许通过 HTTP API 触发重放；生产环境默认关闭，仅运维窗口临时开启。
    /// </summary>
    bool Enabled,

    /// <summary>
    /// 单次 HTTP 调用最多处理的消息数；超过后强制返回，后续交由离线作业继续。
    /// </summary>
    int MaximumSynchronousMessages,

    /// <summary>
    /// 单次 HTTP 重放请求总超时，超时即终止并返回已扫描进度。
    /// </summary>
    TimeSpan ExecutionTimeout);

/// <summary>
/// 范围重放汇总结果；所有计数均为已实际驱动 Inbox 的消息数量，不含仅扫描未入库的 Broker EOF 标记。
/// </summary>
public sealed record KafkaReplayResult(
    /// <summary>
    /// 重放期间从 Broker 实际扫描到的消息总数（不含空 Poll 和 EOF）。
    /// </summary>
    int ScannedMessages,

    /// <summary>
    /// 首次被 Inbox 成功消费并进入 Handler 处理的消息数。
    /// </summary>
    int ProcessedMessages,

    /// <summary>
    /// Inbox 幂等表已存在、被判定为 <c>AlreadyProcessed</c> 的消息数。
    /// </summary>
    int AlreadyProcessedMessages,

    /// <summary>
    /// 契约非法、Handler 永久失败或被永久异常拒绝的消息数；与 DLQ 计数对齐但不自动重投。
    /// </summary>
    int RejectedMessages,

    /// <summary>
    /// 是否已达到 <see cref="KafkaReplayRequest.MaxMessages"/> 而提前停止，true 表示仍有未处理消息。
    /// </summary>
    bool LimitReached);
