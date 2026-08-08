using Confluent.Kafka;
using Full.NET.Data.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 仅在 Inbox 管道返回 Processed/AlreadyProcessed 或 DLQ/Retry 发布成功后手工提交 Kafka Offset。
/// </summary>
internal sealed class KafkaOffsetCommitter
{
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
