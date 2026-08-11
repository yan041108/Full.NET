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
                topicIdentity: topic,
                runId: "run-a");
            checkpoint = await KafkaCapacityCheckpoint.SaveSampleAsync(
                path,
                checkpoint,
                CreateEvidence("sample-incomplete", KafkaCapacitySampleState.Incomplete),
                cancellationToken: CancellationToken.None);
            Assert.IsTrue(File.Exists(path));
            Assert.IsEmpty(checkpoint.CompletedSampleIds);
            checkpoint = await KafkaCapacityCheckpoint.SaveSampleAsync(
                path,
                checkpoint,
                CreateEvidence("sample-1", KafkaCapacitySampleState.Completed),
                cancellationToken: CancellationToken.None);
            checkpoint = await KafkaCapacityCheckpoint.SaveSampleAsync(
                path,
                checkpoint,
                CreateEvidence("sample-2", KafkaCapacitySampleState.Incomplete),
                cancellationToken: CancellationToken.None);
            checkpoint = await KafkaCapacityCheckpoint.SaveSampleAsync(
                path,
                checkpoint,
                CreateEvidence("sample-budget", KafkaCapacitySampleState.Completed) with
                {
                    PerformanceBudgetPassed = false,
                    FailureCodes = ["consumed_rate_budget_not_met"],
                },
                cancellationToken: CancellationToken.None);

            var loaded = await KafkaCapacityCheckpoint.LoadAsync(
                path,
                CancellationToken.None);

            Assert.IsNotNull(loaded);
            CollectionAssert.AreEquivalent(
                new[] { "sample-1" },
                loaded.CompletedSampleIds.ToArray());
            Assert.HasCount(1, loaded.CompletedSamples);
            Assert.AreEqual("sample-1", loaded.CompletedSamples[0].SampleId);
            Assert.IsFalse(File.Exists(path + ".tmp"));
            loaded.ValidateResume(
                "build-a",
                "scenario-a",
                KafkaCapacityScopeCodes.KafkaTransport,
                topic,
                "run-a");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-b",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic,
                    "run-a"));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-a",
                    "scenario-b",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic,
                    "run-a"));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-a",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic with { TopicId = "changed" },
                    "run-a"));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                loaded.ValidateResume(
                    "build-a",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic,
                    "run-b"));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                (loaded with
                {
                    SchemaVersion = KafkaCapacityCheckpoint.CurrentSchemaVersion + 1,
                }).ValidateResume(
                    "build-a",
                    "scenario-a",
                    KafkaCapacityScopeCodes.KafkaTransport,
                    topic,
                    "run-a"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Checkpoint_rejects_missing_nested_evidence_as_invalid_data()
    {
        var checkpoint = KafkaCapacityCheckpoint.Create(
            "build-a",
            "scenario-a",
            KafkaCapacityScopeCodes.KafkaTransport,
            new KafkaCapacityTopicIdentity("cluster-hash", "topic", "topic-id", 1, 1),
            "run-a") with
        {
            CompletedSamples =
            [
                CreateEvidence("sample-1", KafkaCapacitySampleState.Completed) with
                {
                    Integrity = null!,
                },
            ],
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            KafkaCapacityCheckpoint.SaveInitialAsync(
                Path.Combine(Path.GetTempPath(), $"fullnet-checkpoint-{Guid.NewGuid():N}.json"),
                checkpoint,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task Checkpoint_rejects_completed_sample_with_failed_budget()
    {
        var checkpoint = KafkaCapacityCheckpoint.Create(
            "build-a",
            "scenario-a",
            KafkaCapacityScopeCodes.KafkaTransport,
            new KafkaCapacityTopicIdentity("cluster-hash", "topic", "topic-id", 1, 1),
            "run-a") with
        {
            CompletedSamples =
            [
                CreateEvidence("sample-1", KafkaCapacitySampleState.Completed) with
                {
                    PerformanceBudgetPassed = false,
                },
            ],
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            KafkaCapacityCheckpoint.SaveInitialAsync(
                Path.Combine(
                    Path.GetTempPath(),
                    $"fullnet-checkpoint-{Guid.NewGuid():N}.json"),
                checkpoint,
                CancellationToken.None));
    }

    private static KafkaCapacitySampleEvidence CreateEvidence(
        string sampleId,
        KafkaCapacitySampleState state)
    {
        var latency = new KafkaCapacityLatencySnapshot(1, 1, 1, 1, 1, 1, 0);
        return new KafkaCapacitySampleEvidence(
            KafkaCapacityScopeCodes.KafkaTransport,
            sampleId,
            KafkaCapacityScenario.LowRate,
            10,
            64,
            1,
            1,
            state,
            new KafkaCapacityIntegrityEvidence(
                1,
                1,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                DrainCompleted: true),
            new KafkaCapacityPerformanceEvidence(
                1,
                1,
                1,
                latency,
                latency,
                latency,
                1,
                1,
                1,
                0),
            state == KafkaCapacitySampleState.Completed ? [] : ["cancelled"]);
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
