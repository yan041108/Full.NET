using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 观察业务 Consumer Group 在目标 Topic 上的滞后，用于回退前排空证明。
/// </summary>
internal sealed class KafkaConsumerLagObserver
{
    public async Task<bool> WaitUntilDrainedAsync(
        KafkaMessagingOptions kafkaOptions,
        string topicName,
        string consumerGroupId,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerGroupId);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsDrainedAsync(kafkaOptions, topicName, consumerGroupId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        return await IsDrainedAsync(kafkaOptions, topicName, consumerGroupId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task<bool> IsDrainedAsync(
        KafkaMessagingOptions kafkaOptions,
        string topicName,
        string consumerGroupId,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var admin = new AdminClientBuilder(kafkaOptions.BuildClientConfig()).Build();
            var metadata = admin.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            var topic = metadata.Topics.FirstOrDefault(
                candidate => string.Equals(candidate.Topic, topicName, StringComparison.Ordinal));
            if (topic is null || topic.Partitions.Count == 0)
            {
                return true;
            }

            var partitions = topic.Partitions
                .Select(partition => new TopicPartition(
                    topicName,
                    new Partition(partition.PartitionId)))
                .ToList();
            var committed = admin
                .ListConsumerGroupOffsetsAsync(
                    [new ConsumerGroupTopicPartitions(consumerGroupId, partitions)],
                    new ListConsumerGroupOffsetsOptions
                    {
                        RequestTimeout = TimeSpan.FromSeconds(10),
                    })
                .GetAwaiter()
                .GetResult();

            using var consumer = new ConsumerBuilder<Ignore, Ignore>(
                    kafkaOptions.BuildConsumerConfig($"fullnet.lag.probe.{Guid.NewGuid():N}"))
                .Build();
            foreach (var partition in partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var watermark = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
                var highWatermark = watermark.High.Value;
                if (highWatermark == 0)
                {
                    continue;
                }

                var committedGroup = committed.FirstOrDefault(group => string.Equals(
                    group.Group,
                    consumerGroupId,
                    StringComparison.Ordinal));
                if (committedGroup is null || committedGroup.Partitions is null)
                {
                    return false;
                }

                var committedPartitions = committedGroup.Partitions;
                TopicPartitionOffsetError? matchedPartition = null;
                foreach (var candidate in committedPartitions)
                {
                    if (candidate.Topic == topicName
                        && candidate.Partition == partition.Partition)
                    {
                        matchedPartition = candidate;
                        break;
                    }
                }

                if (matchedPartition is not TopicPartitionOffsetError matched)
                {
                    return false;
                }

                var committedOffset = matched.Offset;
                if (committedOffset == Offset.Unset || committedOffset.Value < highWatermark)
                {
                    return false;
                }
            }

            return true;
        }, cancellationToken);
}
