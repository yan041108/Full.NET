using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 使用 Confluent AdminClient 实现容量 Runner 的真实集群和 Topic 控制面。
/// </summary>
public sealed class ConfluentKafkaCapacityAdminClient : IKafkaCapacityAdminClient
{
    private readonly IAdminClient adminClient;
    private readonly TimeSpan requestTimeout;

    public ConfluentKafkaCapacityAdminClient(
        IAdminClient adminClient,
        TimeSpan requestTimeout)
    {
        this.adminClient = adminClient
            ?? throw new ArgumentNullException(nameof(adminClient));
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        this.requestTimeout = requestTimeout;
    }

    public async Task<KafkaCapacityClusterDescription> DescribeClusterAsync(
        CancellationToken cancellationToken)
    {
        var result = await adminClient.DescribeClusterAsync(
                new DescribeClusterOptions { RequestTimeout = requestTimeout })
            .WaitAsync(cancellationToken);
        return new KafkaCapacityClusterDescription(
            result.ClusterId,
            result.Nodes.Count);
    }

    public async Task<KafkaCapacityTopicDescription?> DescribeTopicAsync(
        string topicName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        DescribeTopicsResult result;
        try
        {
            result = await adminClient.DescribeTopicsAsync(
                    TopicCollection.OfTopicNames([topicName]),
                    new DescribeTopicsOptions { RequestTimeout = requestTimeout })
                .WaitAsync(cancellationToken);
        }
        catch (DescribeTopicsException exception)
        {
            var onlyUnknownTopics = true;
            foreach (var description in exception.Results.TopicDescriptions)
            {
                if (description.Error.Code != ErrorCode.UnknownTopicOrPart)
                {
                    onlyUnknownTopics = false;
                    break;
                }
            }

            if (onlyUnknownTopics)
            {
                return null;
            }

            throw;
        }

        var topic = result.TopicDescriptions.Single();
        if (topic.Error.Code == ErrorCode.UnknownTopicOrPart)
        {
            return null;
        }

        if (topic.Error.IsError)
        {
            throw new KafkaException(topic.Error);
        }

        var replicationFactors = topic.Partitions
            .Select(static partition => partition.Replicas.Count)
            .Distinct()
            .ToArray();
        if (replicationFactors.Length != 1)
        {
            throw new InvalidDataException(
                "Kafka capacity Topic has inconsistent partition replication factors.");
        }

        return new KafkaCapacityTopicDescription(
            topic.Name,
            topic.TopicId.ToString(),
            topic.Partitions.Count,
            replicationFactors[0]);
    }

    public async Task CreateTopicAsync(
        string topicName,
        int partitions,
        int replicationFactor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await adminClient.CreateTopicsAsync(
                [
                    new TopicSpecification
                    {
                        Name = topicName,
                        NumPartitions = partitions,
                        ReplicationFactor = checked((short)replicationFactor),
                    },
                ],
                new CreateTopicsOptions
                {
                    RequestTimeout = requestTimeout,
                    OperationTimeout = requestTimeout,
                });
    }

    public async Task DeleteTopicAsync(
        string topicName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await adminClient.DeleteTopicsAsync(
                [topicName],
                new DeleteTopicsOptions
                {
                    RequestTimeout = requestTimeout,
                    OperationTimeout = requestTimeout,
                });
    }
}
