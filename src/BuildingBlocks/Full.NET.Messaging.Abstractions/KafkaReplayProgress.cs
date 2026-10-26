namespace Full.NET.Messaging.Abstractions;

/// <summary>可检查点重放的固定分区窗口；结束位点不包含，恢复位点只在确认 Inbox 结果后前进。</summary>
/// <param name="Partition">目标 Topic 的非负分区号。</param>
/// <param name="StartOffset">初次观察的起始位点。</param>
/// <param name="EndOffsetExclusive">初次观察的结束位点，不随之后追加的消息扩大。</param>
/// <param name="NextOffset">下一条尚未确认的位点。</param>
/// <param name="UnconfirmedOffset">已持久执行意图但尚未确认 Inbox 结果的位点；恢复不得静默跳过。</param>
public sealed record KafkaReplayPartitionRange(int Partition, long StartOffset, long EndOffsetExclusive, long NextOffset,
    long? UnconfirmedOffset = null);

/// <summary>真实 Broker 与 Topic 的稳定身份及固定扫描范围；仅由服务端观察产生。</summary>
/// <param name="ClusterId">真实 Kafka 集群标识。</param>
/// <param name="TopicId">真实 Kafka Topic 标识，防止同名重建误恢复。</param>
/// <param name="Ranges">各分区固定范围和已确认检查点。</param>
public sealed record KafkaReplaySnapshot(string ClusterId, string TopicId, IReadOnlyList<KafkaReplayPartitionRange> Ranges);

/// <summary>单条重放的已确认 Inbox 结果，拒绝不得被当作成功消费检查点。</summary>
public enum KafkaReplayConfirmedOutcome
{
    /// <summary>业务与 Inbox 已成功提交。</summary>
    Processed,
    /// <summary>原 Inbox 已确认同一消息处理完成。</summary>
    AlreadyProcessed,
    /// <summary>契约、路由或业务拒绝，保留当前位置供修复后重试。</summary>
    Rejected,
}

/// <summary>运维进度的持久化回调；回调失败必须停止执行，不能忽略检查点失败继续副作用。</summary>
public interface IKafkaReplayProgressSink
{
    /// <summary>执行首条消息前固定真实资源与范围；恢复时核对原快照。</summary>
    /// <param name="snapshot">Broker 观察或已核对的原快照。</param>
    /// <param name="cancellationToken">用于取消持久化的令牌。</param>
    Task BindSnapshotAsync(KafkaReplaySnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>在驱动 Inbox 前持久执行意图并检查取消、租约和执行代次。</summary>
    /// <param name="partition">当前分区。</param>
    /// <param name="offset">本次消息的真实位点。</param>
    /// <param name="cancellationToken">用于取消持久化的令牌。</param>
    Task BeforeMessageAsync(int partition, long offset, CancellationToken cancellationToken);

    /// <summary>记录实际 Inbox 结果；成功后方可推进该分区检查点。</summary>
    /// <param name="partition">当前分区。</param>
    /// <param name="offset">已处理消息的位点。</param>
    /// <param name="outcome">实际确认结果。</param>
    /// <param name="cancellationToken">用于取消持久化的令牌。</param>
    Task AfterMessageAsync(int partition, long offset, KafkaReplayConfirmedOutcome outcome, CancellationToken cancellationToken);

    /// <summary>观察到分区固定结束边界后确认窗口完成；EOF 不计为业务消息。</summary>
    /// <param name="partition">已观察到边界的分区。</param>
    /// <param name="endOffsetExclusive">初次冻结的结束位点。</param>
    /// <param name="cancellationToken">用于取消持久化的令牌。</param>
    Task CompletePartitionAsync(int partition, long endOffsetExclusive, CancellationToken cancellationToken);
}

/// <summary>原资源或未确认扫描窗口已不可验证，必须拒绝自动扩大或跳过范围。</summary>
public sealed class KafkaReplayWindowUnavailableException : InvalidOperationException
{
    /// <summary>构造不包含连接信息或载荷的受控窗口错误。</summary>
    public KafkaReplayWindowUnavailableException() : base("Kafka replay resource or retained window no longer matches the recorded operation.") { }
}

/// <summary>有真实运维消费者的检查点重放端口；恢复不能重新计算原窗口或改变正式消费组位点。</summary>
public interface ITrackedKafkaReplayService
{
    /// <summary>执行一个有界批次，拒绝消息立即停止，恢复复用原资源与未确认范围。</summary>
    /// <param name="request">经服务端验证的重放请求。</param>
    /// <param name="snapshot">恢复时的原固定快照；首次执行为空。</param>
    /// <param name="progress">持久化执行意图和确认结果的模块端口。</param>
    /// <param name="cancellationToken">本批执行令牌。</param>
    Task<KafkaReplayResult> ReplayTrackedAsync(KafkaReplayRequest request, KafkaReplaySnapshot? snapshot,
        IKafkaReplayProgressSink progress, CancellationToken cancellationToken);
}
