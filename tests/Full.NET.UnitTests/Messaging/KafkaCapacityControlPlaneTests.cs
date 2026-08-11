using Full.NET.Benchmarks.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacityControlPlaneTests
{
    [TestMethod]
    public async Task Topic_manager_creates_unique_topic_and_resumes_only_exact_identity()
    {
        var admin = new RecordingAdminClient("cluster-a", brokerCount: 3);
        var manager = new KafkaCapacityTopicManager(admin);
        var clusterHash = KafkaCapacityFingerprint.Sha256("cluster-a");

        var created = await manager.EnsureTopicAsync(
            "run-a",
            clusterHash,
            partitions: 3,
            replicationFactor: 2,
            resumeIdentity: null,
            CancellationToken.None);

        Assert.AreEqual("fullnet.capacity.run-a.v1", created.TopicName);
        Assert.AreEqual(1, admin.CreateCalls);
        Assert.AreEqual(clusterHash, created.ClusterIdHash);
        Assert.AreEqual(3, created.Partitions);
        Assert.AreEqual(2, created.ReplicationFactor);

        var resumed = await manager.EnsureTopicAsync(
            "run-a",
            clusterHash,
            3,
            2,
            created,
            CancellationToken.None);
        Assert.AreEqual(created, resumed);
        Assert.AreEqual(1, admin.CreateCalls);

        await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
            manager.EnsureTopicAsync(
                "run-a",
                clusterHash,
                3,
                2,
                created with { TopicId = "changed" },
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Topic_manager_rejects_unknown_existing_topic_and_cluster_change()
    {
        var admin = new RecordingAdminClient("cluster-a", 1);
        var manager = new KafkaCapacityTopicManager(admin);
        var clusterHash = KafkaCapacityFingerprint.Sha256("cluster-a");
        var created = await manager.EnsureTopicAsync(
            "run-b",
            clusterHash,
            1,
            1,
            null,
            CancellationToken.None);

        var existingFailure = await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
            manager.EnsureTopicAsync(
                "run-b",
                clusterHash,
                1,
                1,
                null,
                CancellationToken.None));
        Assert.AreEqual("topic_exists", existingFailure.ReasonCode);

        admin.ClusterId = "cluster-b";
        var clusterFailure = await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
            manager.EnsureTopicAsync(
                "run-b",
                clusterHash,
                1,
                1,
                created,
                CancellationToken.None));
        Assert.AreEqual("cluster_identity_changed", clusterFailure.ReasonCode);
    }

    [TestMethod]
    public async Task Topic_manager_deletes_only_after_explicit_exact_revalidation()
    {
        var admin = new RecordingAdminClient("cluster-a", 1);
        var manager = new KafkaCapacityTopicManager(admin);
        var identity = await manager.EnsureTopicAsync(
            "run-c",
            KafkaCapacityFingerprint.Sha256("cluster-a"),
            1,
            1,
            null,
            CancellationToken.None);

        Assert.IsFalse(await manager.DeleteOwnedTopicAsync(
            identity,
            deleteRequested: false,
            CancellationToken.None));
        Assert.AreEqual(0, admin.DeleteCalls);

        admin.Topic = admin.Topic! with { TopicId = "replacement" };
        var failure = await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
            manager.DeleteOwnedTopicAsync(
                identity,
                deleteRequested: true,
                CancellationToken.None));
        Assert.AreEqual("topic_identity_changed", failure.ReasonCode);
        Assert.AreEqual(0, admin.DeleteCalls);

        admin.Topic = new KafkaCapacityTopicDescription(
            identity.TopicName,
            identity.TopicId,
            identity.Partitions,
            identity.ReplicationFactor);
        Assert.IsTrue(await manager.DeleteOwnedTopicAsync(
            identity,
            deleteRequested: true,
            CancellationToken.None));
        Assert.AreEqual(1, admin.DeleteCalls);
    }

    [TestMethod]
    public async Task Checkpoint_persists_only_completed_samples_and_rejects_fingerprint_drift()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fullnet-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "checkpoint.json");
        var topic = new KafkaCapacityTopicIdentity(
            "cluster-hash",
            "topic",
            "topic-id",
            2,
            1);
        try
        {
            var checkpoint = KafkaCapacityCheckpoint.Create(
                "build-a",
                "scenario-a",
                scopeCode: KafkaCapacityScopeCodes.KafkaTransport,
                topicIdentity: topic);
            checkpoint = await KafkaCapacityCheckpoint.SaveCompletedAsync(
                path,
                checkpoint,
                "sample-incomplete",
                sampleCompleted: false,
                scopeCode: KafkaCapacityScopeCodes.KafkaTransport,
                cancellationToken: CancellationToken.None);
            Assert.IsTrue(File.Exists(path));
            Assert.IsEmpty(checkpoint.CompletedSampleIds);
            checkpoint = await KafkaCapacityCheckpoint.SaveCompletedAsync(
                path,
                checkpoint,
                "sample-1",
                sampleCompleted: true,
                scopeCode: KafkaCapacityScopeCodes.KafkaTransport,
                cancellationToken: CancellationToken.None);
            checkpoint = await KafkaCapacityCheckpoint.SaveCompletedAsync(
                path,
                checkpoint,
                "sample-2",
                sampleCompleted: false,
                scopeCode: KafkaCapacityScopeCodes.KafkaTransport,
                cancellationToken: CancellationToken.None);

            var loaded = await KafkaCapacityCheckpoint.LoadAsync(
                path,
                CancellationToken.None);

            Assert.IsNotNull(loaded);
            CollectionAssert.AreEquivalent(
                new[] { "sample-1" },
                loaded.CompletedSampleIds.ToArray());
            Assert.IsFalse(File.Exists(path + ".tmp"));
            loaded.ValidateResume(
                "build-a",
                "scenario-a",
                KafkaCapacityScopeCodes.KafkaTransport,
                topic);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-b",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-a",
                    "scenario-b",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-a",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic with { TopicId = "changed" }));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                (loaded with { SchemaVersion = 2 }).ValidateResume(
                    "build-a",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingAdminClient(
        string clusterId,
        int brokerCount) : IKafkaCapacityAdminClient
    {
        public string ClusterId { get; set; } = clusterId;

        public KafkaCapacityTopicDescription? Topic { get; set; }

        public int CreateCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Task<KafkaCapacityClusterDescription> DescribeClusterAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new KafkaCapacityClusterDescription(
                ClusterId,
                brokerCount));

        public Task<KafkaCapacityTopicDescription?> DescribeTopicAsync(
            string topicName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Topic?.TopicName == topicName ? Topic : null);

        public Task CreateTopicAsync(
            string topicName,
            int partitions,
            int replicationFactor,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            Topic = new KafkaCapacityTopicDescription(
                topicName,
                $"topic-{CreateCalls}",
                partitions,
                replicationFactor);
            return Task.CompletedTask;
        }

        public Task DeleteTopicAsync(
            string topicName,
            CancellationToken cancellationToken)
        {
            DeleteCalls++;
            Topic = null;
            return Task.CompletedTask;
        }
    }
}
