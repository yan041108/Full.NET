using Full.NET.Benchmarks.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacityReportTests
{
    [TestMethod]
    public async Task Budget_rejects_null_entries_as_invalid_data()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-budget-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "environmentName": "Capacity",
                  "clusterIdHash": "cluster-hash",
                  "baselineGitCommit": "base-commit",
                  "generatedAtUtc": "2026-08-12T00:00:00Z",
                  "entries": null
                }
                """);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                KafkaCapacityBudget.LoadAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Budget_requires_exact_environment_and_scenario_then_assesses_thresholds()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fullnet-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "budget.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "environmentName": "Capacity",
                  "clusterIdHash": "cluster-hash",
                  "baselineGitCommit": "base-commit",
                  "generatedAtUtc": "2026-08-12T00:00:00Z",
                  "entries": [
                    {
                      "scopeCode": "kafka_transport",
                      "scenario": "LowRate",
                      "targetMessagesPerSecond": 10,
                      "payloadSizeBytes": 256,
                      "partitions": 2,
                      "producerConcurrency": 1,
                      "minimumScheduledMessagesPerSecond": 9,
                      "minimumAcknowledgedMessagesPerSecond": 9,
                      "minimumConsumedMessagesPerSecond": 9,
                      "maximumScheduleP95Microseconds": 900,
                      "maximumScheduleP99Microseconds": 1000,
                      "maximumAcknowledgementP95Microseconds": 1900,
                      "maximumAcknowledgementP99Microseconds": 2000,
                      "maximumEndToEndP95Microseconds": 2900,
                      "maximumEndToEndP99Microseconds": 3000,
                      "maximumDrainMilliseconds": 5000,
                      "maximumCpuPercent": 80,
                      "maximumManagedHeapBytes": 1000000,
                      "maximumLocalQueueMessages": 100
                    }
                  ]
                }
                """);
            var budget = await KafkaCapacityBudget.LoadAsync(
                path,
                CancellationToken.None);
            var planned = KafkaCapacityScenarioCatalog.Build(
                KafkaCapacityOptions.Parse([
                    "--scenarios", "low-rate",
                    "--low-rates", "10",
                    "--payload-sizes", "256",
                    "--producer-concurrency", "1",
                    "--partitions", "2",
                ]));
            budget.ValidateCoverage(
                "Capacity",
                "cluster-hash",
                "base-commit",
                partitions: 2,
                planned);
            Assert.AreEqual(
                1_000L,
                budget.ResolveScheduleLatencyLimitMicroseconds(
                    planned[0],
                    partitions: 2,
                    defaultLimitMicroseconds: 5_000_000));
            Assert.AreEqual(64, budget.Fingerprint.Length);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                budget.ValidateCoverage(
                    "Capacity",
                    "cluster-hash",
                    "base-commit",
                    partitions: 2,
                    [planned[0] with { TargetMessagesPerSecond = 11 }]));
            var sample = CreateCompletedSample();

            var passed = budget.Assess(
                "Capacity",
                "cluster-hash",
                "base-commit",
                sample);
            var failed = budget.Assess(
                "Capacity",
                "cluster-hash",
                "base-commit",
                sample with
                {
                    Performance = sample.Performance with
                    {
                        EndToEndLatency = sample.Performance.EndToEndLatency with
                        {
                            P99Microseconds = 3_001,
                        },
                    },
                });

            Assert.IsTrue(passed.Passed);
            Assert.IsFalse(failed.Passed);
            CollectionAssert.Contains(
                failed.FailureCodes.ToArray(),
                "end_to_end_p99_budget_exceeded");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                budget.Assess(
                    "Other",
                    "cluster-hash",
                    "base-commit",
                    sample));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Report_writer_emits_allowlisted_incomplete_evidence_without_secrets()
    {
        const string bootstrap = "private-broker:9093";
        const string username = "private-user";
        const string password = "private-password";
        const string topicName = "fullnet.capacity.secret-topic";
        var directory = Path.Combine(Path.GetTempPath(), $"fullnet-report-{Guid.NewGuid():N}");
        var kafka = new KafkaMessagingOptions
        {
            Enabled = true,
            BootstrapServers = bootstrap,
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "ScramSha512",
            SaslUsername = username,
            SaslPassword = password,
        };
        var topic = new KafkaCapacityTopicIdentity(
            "cluster-hash",
            topicName,
            "topic-id",
            2,
            1);
        var manifest = KafkaCapacityReportProjection.CreateManifest(
            "Capacity",
            "build",
            "run-secret",
            "approval-secret",
            kafka,
            topic);
        var incomplete = CreateCompletedSample() with
        {
            State = KafkaCapacitySampleState.Incomplete,
            FailureCodes = ["drain_timeout"],
        };
        var statistics = KafkaCapacityLibrdkafkaStatisticsProjection.Parse(
            $$"""
            {
              "name": "{{bootstrap}}/{{username}}/{{password}}/{{topicName}}",
              "msg_cnt": 7,
              "msg_size": 2048,
              "txmsgs": 11,
              "rxmsgs": 9,
              "txbytes": 4096,
              "rxbytes": 3072,
              "brokers": {
                "{{bootstrap}}": {
                  "state": "UP",
                  "outbuf_cnt": 2,
                  "waitresp_cnt": 3,
                  "txerrs": 4,
                  "rxerrs": 5,
                  "req_timeouts": 6,
                  "rtt": { "avg": 700, "max": 900 }
                }
              }
            }
            """,
            sampleId: "sample-a",
            phase: "measurement");

        Assert.AreEqual("sample-a", statistics.SampleId);
        Assert.AreEqual("measurement", statistics.Phase);
        Assert.AreEqual(1, statistics.ConnectedBrokerCount);
        Assert.AreEqual(700L, statistics.RequestLatencyAverageMicroseconds);
        Assert.AreEqual(900L, statistics.RequestLatencyMaximumMicroseconds);
        Assert.AreEqual(15L, statistics.ErrorCount);

        try
        {
            await KafkaCapacityReportWriter.WriteAsync(
                directory,
                new KafkaCapacityReportEvidence(
                    manifest,
                    [incomplete],
                    [statistics]),
                CancellationToken.None);

            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            var combined = string.Join(
                "\n",
                files.Select(File.ReadAllText));
            Assert.IsFalse(combined.Contains(bootstrap, StringComparison.Ordinal));
            Assert.IsFalse(combined.Contains(username, StringComparison.Ordinal));
            Assert.IsFalse(combined.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(combined.Contains(topicName, StringComparison.Ordinal));
            StringAssert.Contains(combined, "KafkaTransport");
            StringAssert.Contains(combined, "Capacity-not-verified");
            StringAssert.Contains(combined, "drain_timeout");
            StringAssert.Contains(combined, "Incomplete");
            StringAssert.Contains(combined, "\"messageCount\":7");
            Assert.IsFalse(files.Any(static path =>
                path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual(
                KafkaCapacityExitCode.DependencyOrIncomplete,
                KafkaCapacityExitCodeResolver.Resolve([incomplete], budgetProvided: false));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Exit_code_resolver_prioritizes_correctness_then_incomplete_and_budget()
    {
        var completed = CreateCompletedSample();
        var corrupted = completed with
        {
            Integrity = completed.Integrity with { Corrupted = 1 },
        };

        Assert.AreEqual(
            KafkaCapacityExitCode.CorrectnessFailed,
            KafkaCapacityExitCodeResolver.Resolve(
                [corrupted],
                budgetProvided: false));
        Assert.AreEqual(
            KafkaCapacityExitCode.DependencyOrIncomplete,
            KafkaCapacityExitCodeResolver.Resolve(
                [completed with { State = KafkaCapacitySampleState.Incomplete }],
                budgetProvided: false));
        Assert.AreEqual(
            KafkaCapacityExitCode.PerformanceBudgetFailed,
            KafkaCapacityExitCodeResolver.Resolve(
                [completed with { PerformanceBudgetPassed = false }],
                budgetProvided: true));
        Assert.AreEqual(
            KafkaCapacityExitCode.Success,
            KafkaCapacityExitCodeResolver.Resolve(
                [completed],
                budgetProvided: false));
    }

    private static KafkaCapacitySampleEvidence CreateCompletedSample()
    {
        var latency = new KafkaCapacityLatencySnapshot(
            10,
            10,
            1_000,
            500,
            800,
            900,
            0);
        return new KafkaCapacitySampleEvidence(
            KafkaCapacityScopeCodes.KafkaTransport,
            "sample-a",
            KafkaCapacityScenario.LowRate,
            10,
            256,
            2,
            1,
            KafkaCapacitySampleState.Completed,
            new KafkaCapacityIntegrityEvidence(
                10,
                10,
                10,
                0,
                0,
                0,
                0,
                0,
                0,
                true),
            new KafkaCapacityPerformanceEvidence(
                10,
                10,
                10,
                latency,
                latency,
                latency,
                100,
                10,
                100_000,
                1),
            []);
    }
}
