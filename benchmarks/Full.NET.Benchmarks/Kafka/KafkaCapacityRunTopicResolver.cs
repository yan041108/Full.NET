namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 解析 Scope C Debezium 路由 Topic 身份，并在需要时预创建 Topic。
/// </summary>
public static class KafkaCapacityRunTopicResolver
{
    public static async Task<KafkaCapacityTopicIdentity> EnsureTopicAsync(
        IKafkaCapacityAdminClient adminClient,
        string clusterIdHash,
        string topicName,
        int partitions,
        int replicationFactor,
        KafkaCapacityTopicIdentity? resumeIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterIdHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        var cluster = await adminClient.DescribeClusterAsync(cancellationToken)
            .ConfigureAwait(false);
        var actualClusterHash = KafkaCapacityFingerprint.Sha256(cluster.ClusterId);
        if (!string.Equals(clusterIdHash, actualClusterHash, StringComparison.Ordinal))
        {
            throw new KafkaCapacityControlPlaneException(
                "cluster_identity_changed",
                "Kafka cluster identity changed before Scope C topic validation.");
        }

        if (cluster.BrokerCount < replicationFactor)
        {
            throw new KafkaCapacityControlPlaneException(
                "insufficient_brokers",
                "Kafka broker count is below the requested replication factor.");
        }

        var current = await adminClient.DescribeTopicAsync(topicName, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            if (resumeIdentity is not null)
            {
                throw new KafkaCapacityControlPlaneException(
                    "resume_topic_missing",
                    "The checkpoint Scope C topic no longer exists.");
            }

            await adminClient.CreateTopicAsync(
                topicName,
                partitions,
                replicationFactor,
                cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                current = await adminClient.DescribeTopicAsync(topicName, cancellationToken)
                    .ConfigureAwait(false);
                if (current is not null)
                {
                    break;
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            current ??= await adminClient.DescribeTopicAsync(topicName, cancellationToken)
                .ConfigureAwait(false);
            if (current is null)
            {
                throw new KafkaCapacityControlPlaneException(
                    "topic_create_failed",
                    "Scope C topic creation did not produce a describable topic.");
            }
        }
        else if (resumeIdentity is not null)
        {
            if (!string.Equals(resumeIdentity.TopicName, current.TopicName, StringComparison.Ordinal)
                || !string.Equals(resumeIdentity.TopicId, current.TopicId, StringComparison.Ordinal)
                || resumeIdentity.Partitions != current.Partitions
                || resumeIdentity.ReplicationFactor != current.ReplicationFactor)
            {
                throw new KafkaCapacityControlPlaneException(
                    "topic_identity_changed",
                    "Scope C checkpoint topic identity no longer matches the cluster.");
            }
        }

        return new KafkaCapacityTopicIdentity(
            clusterIdHash,
            current.TopicName,
            current.TopicId,
            current.Partitions,
            current.ReplicationFactor);
    }
}
