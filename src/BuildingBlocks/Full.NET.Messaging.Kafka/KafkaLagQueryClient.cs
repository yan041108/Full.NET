using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Full.NET.Messaging.Kafka;

/// <summary>一次采样拥有一个客户端；每次异步请求有 SDK 超时，调用方须等待在途请求结束后释放。</summary>
internal interface IKafkaLagQueryClient : IDisposable
{
    Task<IReadOnlyList<TopicPartition>> ReadPartitionsAsync(string topic);
    Task<IReadOnlyDictionary<TopicPartition, long>> ReadCommittedOffsetsAsync(string group, IReadOnlyList<TopicPartition> partitions);
    Task<IReadOnlyDictionary<TopicPartition, long>> ReadEndOffsetsAsync(IReadOnlyList<TopicPartition> partitions);
}

/// <summary>使用原生 Admin 异步接口批量读取，不创建临时 Consumer 或线程池阻塞任务。</summary>
internal sealed class KafkaLagQueryClient(KafkaMessagingOptions options) : IKafkaLagQueryClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly IAdminClient _admin = new AdminClientBuilder(options.BuildClientConfig()).Build();

    public async Task<IReadOnlyList<TopicPartition>> ReadPartitionsAsync(string topic)
    {
        var result = await _admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]),
            new DescribeTopicsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        var description = result.TopicDescriptions.SingleOrDefault(item => item.Name == topic);
        if (description is null || description.Error.IsError) return [];
        return description.Partitions.Select(partition => new TopicPartition(topic, partition.Partition)).ToArray();
    }

    public async Task<IReadOnlyDictionary<TopicPartition, long>> ReadCommittedOffsetsAsync(string group, IReadOnlyList<TopicPartition> partitions)
    {
        var result = await _admin.ListConsumerGroupOffsetsAsync(
            [new ConsumerGroupTopicPartitions(group, partitions.ToList())],
            new ListConsumerGroupOffsetsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        var offsets = result.SingleOrDefault(item => item.Group == group)?.Partitions;
        if (offsets is null) return new Dictionary<TopicPartition, long>();
        if (offsets.Any(item => item.Error.IsError)) throw new InvalidOperationException("消费组提交位置查询失败。");
        return offsets.ToDictionary(item => item.TopicPartition, item => item.Offset.Value);
    }

    public async Task<IReadOnlyDictionary<TopicPartition, long>> ReadEndOffsetsAsync(IReadOnlyList<TopicPartition> partitions)
    {
        var result = await _admin.ListOffsetsAsync(partitions.Select(partition => new TopicPartitionOffsetSpec
        {
            TopicPartition = partition, OffsetSpec = OffsetSpec.Latest(),
        }), new ListOffsetsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        var offsets = result.ResultInfos.Select(item => item.TopicPartitionOffsetError).ToArray();
        if (offsets.Any(item => item.Error.IsError)) throw new InvalidOperationException("分区高水位查询失败。");
        return offsets.ToDictionary(item => item.TopicPartition, item => item.Offset.Value);
    }

    public void Dispose() => _admin.Dispose();
}
