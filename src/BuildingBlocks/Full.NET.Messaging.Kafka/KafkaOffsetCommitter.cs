using Confluent.Kafka;
using Full.NET.Data.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 仅在 Inbox 管道返回 Processed/AlreadyProcessed 或 DLQ/Retry 发布成功后手工提交 Kafka Offset。
/// </summary>
internal sealed class KafkaOffsetCommitter
{
    public bool ShouldCommit(InboxConsumeResult inboxResult)
    {
        ArgumentNullException.ThrowIfNull(inboxResult);
        return inboxResult.Status is InboxConsumeStatus.Processed
            or InboxConsumeStatus.AlreadyProcessed;
    }
}
