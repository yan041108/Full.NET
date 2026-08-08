using Confluent.Kafka;
using Full.NET.Data.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 仅在 Inbox 管道返回 Processed/AlreadyProcessed 后手工提交 Kafka Offset。
/// </summary>
internal sealed class KafkaOffsetCommitter
{
    /// <summary>
    /// 瞬态或永久性失败均不提交；DLQ 发布留待 Task 6。
    /// </summary>
    public bool TryCommit(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> consumeResult,
        InboxConsumeResult inboxResult)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(consumeResult);
        ArgumentNullException.ThrowIfNull(inboxResult);

        if (inboxResult.Status is not (InboxConsumeStatus.Processed or InboxConsumeStatus.AlreadyProcessed))
        {
            return false;
        }

        consumer.Commit(consumeResult);
        return true;
    }
}