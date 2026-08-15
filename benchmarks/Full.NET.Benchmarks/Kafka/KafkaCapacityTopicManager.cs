namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 抽象容量 Runner 所需的 Kafka Admin 操作，便于故障和所有权验证。
/// </summary>
public interface IKafkaCapacityAdminClient
{
    Task<KafkaCapacityClusterDescription> DescribeClusterAsync(
        CancellationToken cancellationToken);

    Task<KafkaCapacityTopicDescription?> DescribeTopicAsync(
        string topicName,
        CancellationToken cancellationToken);

    Task CreateTopicAsync(
        string topicName,
        int partitions,
        int replicationFactor,
        CancellationToken cancellationToken);

    Task DeleteTopicAsync(
        string topicName,
        CancellationToken cancellationToken);
}

/// <summary>
/// 表示 Topic 所有权或集群身份保护拒绝。
/// </summary>
public sealed class KafkaCapacityControlPlaneException(
    string reasonCode,
    string message) : InvalidOperationException(message)
{
    public string ReasonCode { get; } = reasonCode;
}

/// <summary>
/// 创建、续跑和删除唯一临时 Topic，并在每次破坏性动作前重新校验身份。
/// </summary>
public sealed class KafkaCapacityTopicManager(IKafkaCapacityAdminClient adminClient)
{
    public async Task<KafkaCapacityTopicIdentity> EnsureTopicAsync(
        string runId,
        string expectedClusterIdHash,
        int partitions,
        int replicationFactor,
        KafkaCapacityTopicIdentity? resumeIdentity,
        CancellationToken cancellationToken)
    {
        var topicName = BuildTopicName(runId);
        var cluster = await adminClient.DescribeClusterAsync(cancellationToken);
        var actualClusterHash = KafkaCapacityFingerprint.Sha256(cluster.ClusterId);
        if (!string.Equals(
                expectedClusterIdHash,
                actualClusterHash,
                StringComparison.Ordinal))
        {
            throw Rejected(
                "cluster_identity_changed",
                "Kafka cluster identity changed before Topic ownership validation.");
        }

        if (cluster.BrokerCount < replicationFactor)
        {
            throw Rejected(
                "insufficient_brokers",
                "Kafka broker count is below the requested replication factor.");
        }

        var current = await adminClient.DescribeTopicAsync(
            topicName,
            cancellationToken);
        if (current is null)
        {
            if (resumeIdentity is not null)
            {
                throw Rejected(
                    "resume_topic_missing",
                    "The checkpoint Topic no longer exists.");
            }

            await adminClient.CreateTopicAsync(
                topicName,
                partitions,
                replicationFactor,
                cancellationToken);
            current = await adminClient.DescribeTopicAsync(
                topicName,
                CancellationToken.None)
                ?? throw Rejected(
                    "topic_create_incomplete",
                    "Kafka did not expose the created Topic identity.");
        }
        else if (resumeIdentity is null)
        {
            throw Rejected(
                "topic_exists",
                "The generated Kafka capacity Topic already exists without a checkpoint.");
        }

        var identity = new KafkaCapacityTopicIdentity(
            actualClusterHash,
            current.TopicName,
            current.TopicId,
            current.Partitions,
            current.ReplicationFactor);
        if (resumeIdentity is not null && identity != resumeIdentity)
        {
            throw Rejected(
                "topic_identity_changed",
                "Kafka Topic identity differs from the checkpoint.");
        }

        if (current.Partitions != partitions
            || current.ReplicationFactor != replicationFactor)
        {
            throw Rejected(
                "topic_shape_changed",
                "Kafka Topic partition or replication shape is not approved.");
        }

        return identity;
    }

    public async Task<bool> DeleteOwnedTopicAsync(
        KafkaCapacityTopicIdentity identity,
        bool deleteRequested,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!deleteRequested)
        {
            return false;
        }

        var cluster = await adminClient.DescribeClusterAsync(cancellationToken);
        if (!string.Equals(
                KafkaCapacityFingerprint.Sha256(cluster.ClusterId),
                identity.ClusterIdHash,
                StringComparison.Ordinal))
        {
            throw Rejected(
                "cluster_identity_changed",
                "Kafka cluster identity changed before Topic deletion.");
        }

        var current = await adminClient.DescribeTopicAsync(
            identity.TopicName,
            cancellationToken);
        var currentIdentity = current is null
            ? null
            : new KafkaCapacityTopicIdentity(
                identity.ClusterIdHash,
                current.TopicName,
                current.TopicId,
                current.Partitions,
                current.ReplicationFactor);
        if (currentIdentity != identity)
        {
            throw Rejected(
                "topic_identity_changed",
                "Kafka Topic identity changed before deletion.");
        }

        await adminClient.DeleteTopicAsync(
            identity.TopicName,
            cancellationToken);
        return true;
    }

    private static string BuildTopicName(string runId)
    {
        var safeRunId = NormalizeSegment(runId, nameof(runId));
        return $"fullnet.capacity.{safeRunId}.v1";
    }

    private static string NormalizeSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character == '-'
                    ? character
                    : '-')
            .ToArray());
        if (normalized.Length > 80)
        {
            normalized = normalized[..80];
        }

        return normalized;
    }

    private static KafkaCapacityControlPlaneException Rejected(
        string reasonCode,
        string message) =>
        new(reasonCode, message);
}
