using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Provider 低基数指标；禁止 MessageId、TenantId、原始 Topic 或异常文本标签。
/// </summary>
public static class KafkaMessagingTelemetry
{
    public const string MeterName = "Full.NET.Messaging";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ConsumeResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.consume.results");
    private static readonly Counter<long> CommitResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.commit.results");
    private static readonly Counter<long> PartitionFlowResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.partition.flow.results");

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
    }

    public static void RecordCommit(
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string result)
    {
        Record(
            CommitResults,
            provider,
            topicCode,
            consumerCode,
            messageTypeCode,
            result,
            reasonCode: null);
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
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响分区背压与 Offset 语义。
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
}
