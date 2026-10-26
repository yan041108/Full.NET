namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>先创建再执行的范围重放；RequestId 是创建重试键，偏移量采用十进制字符串避免浏览器精度损失。</summary>
/// <param name="RequestId">客户端为一次创建意图生成并在重试时复用的 UUID；不是数据库主键。</param>
/// <param name="TopicCode">已登记 Topic。</param>
/// <param name="FromTimestampUtc">UTC 时间范围起点；与偏移范围二选一。</param>
/// <param name="ToTimestampUtc">UTC 时间范围终点。</param>
/// <param name="FromOffset">精确偏移起点的十进制字符串。</param>
/// <param name="ToOffset">精确偏移终点的十进制字符串，含终点。</param>
/// <param name="Partitions">目标分区；空数组表示创建执行窗口时的全部分区。</param>
/// <param name="ReplayConsumerName">原业务 Inbox 订阅名。</param>
/// <param name="MaxMessages">每次有界执行的扫描上限；部分完成后可按原窗口继续。</param>
/// <param name="Reason">不可变创建理由；操作者从认证上下文解析。</param>
public sealed record CreateKafkaReplayOperationRequest(Guid RequestId, string TopicCode, DateTimeOffset? FromTimestampUtc,
    DateTimeOffset? ToTimestampUtc, string? FromOffset, string? ToOffset, IReadOnlyList<int> Partitions,
    string ReplayConsumerName, int MaxMessages, string Reason);

/// <summary>执行、恢复或取消操作的理由；不能提交资源快照、检查点或操作者。</summary>
/// <param name="Reason">本次操作的可追溯理由。</param>
public sealed record ChangeKafkaReplayOperationRequest(string Reason);

/// <summary>已固定分区范围的只读投影；全部位点以十进制字符串传输。</summary>
/// <param name="Partition">分区号。</param>
/// <param name="StartOffset">原窗口起点。</param>
/// <param name="EndOffsetExclusive">固定结束位点，不包含。</param>
/// <param name="NextOffset">下次需要确认的位点。</param>
public sealed record KafkaReplayPartitionResponse(int Partition, string StartOffset, string EndOffsetExclusive, string NextOffset);

/// <summary>可查询的持久重放操作；计数为已持久确认的结果，未知在途结果不伪装为零副作用。</summary>
/// <param name="OperationId">服务端生成的 UUID v7。</param>
/// <param name="State">稳定状态码，过期执行显示 result_unknown。</param>
/// <param name="TopicCode">目标 Topic。</param>
/// <param name="ReplayConsumerName">原订阅名。</param>
/// <param name="EventType">权威目录事件类型。</param>
/// <param name="SchemaVersion">权威目录结构版本。</param>
/// <param name="MaxMessages">每次执行的扫描上限。</param>
/// <param name="ConfirmedMessages">已确认消费结果数，不含拒绝尝试。</param>
/// <param name="ProcessedMessages">已确认的新业务提交数。</param>
/// <param name="AlreadyProcessedMessages">已确认的 Inbox 去重数。</param>
/// <param name="RejectedAttempts">被拒绝的尝试数，修复前重复尝试可能增加。</param>
/// <param name="AttemptCount">执行代次数。</param>
/// <param name="HasUnconfirmedMessage">存在已持久执行意图但未确认结果的消息。</param>
/// <param name="LastReasonCode">稳定机器原因，不包含原始异常或连接信息。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
/// <param name="UpdatedAtUtc">最近持久进度时间。</param>
/// <param name="Partitions">已固定窗口；执行前为空。</param>
public sealed record KafkaReplayOperationResponse(Guid OperationId, string State, string TopicCode, string ReplayConsumerName,
    string EventType, int SchemaVersion, int MaxMessages, long ConfirmedMessages, long ProcessedMessages,
    long AlreadyProcessedMessages, long RejectedAttempts, int AttemptCount, bool HasUnconfirmedMessage,
    string? LastReasonCode, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<KafkaReplayPartitionResponse> Partitions);

/// <summary>重放操作状态机；未知结果需要以原窗口和 Inbox 幂等确认，不能直接宣称全部失败。</summary>
public static class KafkaReplayOperationStates
{
    /// <summary>已创建，尚未执行。</summary>
    public const string Prepared = "prepared";
    /// <summary>某执行代次正在处理。</summary>
    public const string Running = "running";
    /// <summary>本次有界执行结束，原窗口仍未全部确认。</summary>
    public const string Partial = "partial";
    /// <summary>固定窗口全部确认且终态审计已提交。</summary>
    public const string Completed = "completed";
    /// <summary>已请求取消，在途结果仍须确认。</summary>
    public const string CancelRequested = "cancel_requested";
    /// <summary>已停止执行，保留确认进度，允许显式恢复。</summary>
    public const string Cancelled = "cancelled";
    /// <summary>执行中断或租约过期，存在未确认结果。</summary>
    public const string ResultUnknown = "result_unknown";
    /// <summary>原资源或保留窗口无法证明，停止恢复。</summary>
    public const string Blocked = "blocked";
}
