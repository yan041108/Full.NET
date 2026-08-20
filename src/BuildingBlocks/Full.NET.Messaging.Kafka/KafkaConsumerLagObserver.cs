using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 观察业务 Consumer Group 在目标 Topic 上的滞后，用于回退前排空证明与低基数 lag 指标。
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
            var lag = await ObserveLagAsync(
                    kafkaOptions,
                    topicName,
                    consumerGroupId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (lag is { TotalLagMessages: 0 })
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        var finalLag = await ObserveLagAsync(
                kafkaOptions,
                topicName,
                consumerGroupId,
                cancellationToken)
            .ConfigureAwait(false);
        return finalLag is { TotalLagMessages: 0 };
    }

    /// <summary>
    /// 采样一次 Group 滞后并写入 <see cref="KafkaMessagingTelemetry"/>；失败时返回 null。
    /// </summary>
    /// <remarks>
    /// <paramref name="lagRetentionRatioOverride"/> 由平台按“最老未消费年龄 / Topic 保留期”计算后传入；
    /// 未提供时写入 0，避免用消息条数冒充时间占比触发假告警。
    /// </remarks>
    public Task<KafkaConsumerLagSnapshot?> ObserveLagAsync(
        KafkaMessagingOptions kafkaOptions,
        string topicName,
        string consumerGroupId,
        CancellationToken cancellationToken,
        double? lagRetentionRatioOverride = null) =>
        Task.Run(() =>
        {
            try
            {
                using var admin = new AdminClientBuilder(kafkaOptions.BuildClientConfig()).Build();
                var metadata = admin.GetMetadata(topicName, TimeSpan.FromSeconds(10));
                var topic = metadata.Topics.FirstOrDefault(
                    candidate => string.Equals(candidate.Topic, topicName, StringComparison.Ordinal));
                if (topic is null || topic.Partitions.Count == 0)
                {
                    var emptyRatio = lagRetentionRatioOverride ?? 0d;
                    KafkaMessagingTelemetry.UpdateConsumerLag(
                        "kafka",
                        consumerGroupId,
                        lagMessages: 0,
                        lagRetentionRatio: emptyRatio);
                    return new KafkaConsumerLagSnapshot(0, emptyRatio);
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

                long totalLag = 0;
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
                        totalLag += highWatermark;
                        continue;
                    }

                    TopicPartitionOffsetError? matchedPartition = null;
                    foreach (var candidate in committedGroup.Partitions)
                    {
                        if (candidate.Topic == topicName
                            && candidate.Partition == partition.Partition)
                        {
                            matchedPartition = candidate;
                            break;
                        }
                    }

                    if (matchedPartition is not TopicPartitionOffsetError matched
                        || matched.Offset == Offset.Unset)
                    {
                        totalLag += highWatermark;
                        continue;
                    }

                    totalLag += Math.Max(0L, highWatermark - matched.Offset.Value);
                }

                var ratio = lagRetentionRatioOverride ?? 0d;
                KafkaMessagingTelemetry.UpdateConsumerLag(
                    "kafka",
                    consumerGroupId,
                    totalLag,
                    ratio);
                return new KafkaConsumerLagSnapshot(totalLag, ratio);
            }
            catch (Exception)
            {
                // lag 探针失败不得阻断回退/排空控制面；调用方按未排空处理。
                return null;
            }
        }, cancellationToken);
}

/// <summary>单次 Consumer lag 采样结果。</summary>
/// <param name="TotalLagMessages">各分区滞后消息数之和。</param>
/// <param name="LagRetentionRatio">相对保留窗口的近似占比，供近保留告警使用。</param>
internal sealed record KafkaConsumerLagSnapshot(
    long TotalLagMessages,
    double LagRetentionRatio);
