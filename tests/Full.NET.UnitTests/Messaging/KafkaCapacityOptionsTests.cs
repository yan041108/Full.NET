using Full.NET.Benchmarks.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacityOptionsTests
{
    [TestMethod]
    public void Defaults_build_two_bounded_transport_samples()
    {
        var options = KafkaCapacityOptions.Parse([]);

        var samples = KafkaCapacityScenarioCatalog.Build(options);

        Assert.IsFalse(options.Execute);
        Assert.HasCount(2, samples);
        Assert.IsTrue(samples.Any(sample =>
            sample.Scenario == KafkaCapacityScenario.LowRate
            && sample.TargetMessagesPerSecond == 10));
        Assert.IsTrue(samples.Any(sample =>
            sample.Scenario == KafkaCapacityScenario.Throughput
            && sample.TargetMessagesPerSecond == 1_000));
        Assert.IsTrue(samples.All(sample =>
            sample.ScopeCode == KafkaCapacityScopeCodes.KafkaTransport));
        Assert.AreEqual(
            "low-rate-r10-p256-c1-n1",
            samples[0].SampleId);
    }

    [TestMethod]
    public void Defaults_use_a_unique_artifact_directory_per_parse()
    {
        var first = KafkaCapacityOptions.Parse([]);
        var second = KafkaCapacityOptions.Parse([]);

        Assert.AreNotEqual(first.OutputDirectory, second.OutputDirectory);
        StringAssert.Contains(first.OutputDirectory, "kafka-capacity");
        StringAssert.Contains(second.OutputDirectory, "kafka-capacity");
    }

    [TestMethod]
    public void Parser_accepts_explicit_bounded_matrix()
    {
        var options = KafkaCapacityOptions.Parse([
            "--scenarios", "low-rate,throughput",
            "--low-rates", "1,10",
            "--throughput-rates", "1000,5000",
            "--payload-sizes", "128,4096",
            "--producer-concurrency", "1,4",
            "--partitions", "12",
            "--replication-factor", "3",
            "--repetitions", "2",
            "--warmup-seconds", "5",
            "--duration-seconds", "15",
            "--drain-seconds", "20",
            "--max-messages-per-sample", "10000",
            "--resume", "true",
            "--max-new-samples", "4",
            "--delete-topic", "true",
            "--execute", "true",
            "--settings", "capacity.settings.json",
            "--budget", "budget.json",
            "--approval-id", "PERF-42",
            "--reason", "dedicated-baseline",
            "--run-id", "run-42",
            "--output", "artifacts/kafka",
        ]);

        Assert.IsTrue(options.Execute);
        Assert.AreEqual(12, options.Partitions);
        Assert.AreEqual(3, options.ReplicationFactor);
        Assert.AreEqual(TimeSpan.FromSeconds(15), options.Duration);
        Assert.AreEqual("PERF-42", options.ApprovalId);
        Assert.AreEqual("run-42", options.RunId);
        Assert.HasCount(32, KafkaCapacityScenarioCatalog.Build(options));
    }

    [TestMethod]
    public void Parser_rejects_empty_list_segments()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--throughput-rates", "1000,,5000",
            ]));
    }

    [TestMethod]
    public void Parser_rejects_duplicate_and_descending_values()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--scenarios", "throughput,throughput",
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--throughput-rates", "5000,1000",
            ]));
    }

    [TestMethod]
    public void Parser_rejects_values_outside_hard_bounds()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityOptions.Parse([
                "--partitions", "129",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityOptions.Parse([
                "--payload-sizes", "63",
            ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityOptions.Parse([
                "--throughput-rates", "1000001",
            ]));
    }

    [TestMethod]
    public void Parser_rejects_resume_budget_without_resume()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--resume", "false",
                "--max-new-samples", "1",
            ]));
    }

    [TestMethod]
    public void Parser_rejects_total_message_and_duration_budgets()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--scenarios", "throughput",
                "--throughput-rates", "1000000",
                "--payload-sizes", "64,128",
                "--duration-seconds", "3600",
                "--max-messages-per-sample", "100000000",
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--repetitions", "20",
                "--duration-seconds", "3600",
                "--max-messages-per-sample", "1",
            ]));
    }

    [TestMethod]
    public void Parser_rejects_unknown_or_repeated_options()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--unknown", "value",
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            KafkaCapacityOptions.Parse([
                "--execute", "true",
                "--execute", "false",
            ]));
    }

    [TestMethod]
    public void Configuration_loads_settings_and_environment_overrides()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-kafka-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "capacity.json");
        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "KafkaCapacity": {
                    "ExecutionEnabled": false,
                    "EnvironmentName": "Capacity",
                    "ExpectedClusterId": "cluster-file",
                    "Kafka": {
                      "Enabled": true,
                      "BootstrapServers": "file-broker:9093",
                      "SecurityProtocol": "SaslSsl",
                      "SaslMechanism": "ScramSha512",
                      "SaslUsername": "file-user",
                      "SaslPassword": "file-secret"
                    }
                  }
                }
                """);
            var environment = new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["KafkaCapacity__ExecutionEnabled"] = "true",
                ["KafkaCapacity__ExpectedClusterId"] = "cluster-env",
                ["KafkaCapacity__Kafka__BootstrapServers"] = "env-broker:9093",
                ["KafkaCapacity__Kafka__SaslPassword"] = "env-secret",
            };

            var configuration = KafkaCapacityConfiguration.Load(
                KafkaCapacityOptions.Parse([
                    "--settings", settingsPath,
                ]),
                environment.GetValueOrDefault);

            Assert.IsTrue(configuration.ExecutionEnabled);
            Assert.AreEqual("cluster-env", configuration.ExpectedClusterId);
            Assert.AreEqual("env-broker:9093", configuration.Kafka.BootstrapServers);
            Assert.AreEqual("env-secret", configuration.Kafka.SaslPassword);
            Assert.IsFalse(configuration.ToString().Contains("env-secret", StringComparison.Ordinal));
            Assert.IsFalse(configuration.ToString().Contains("env-broker:9093", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Environment_guard_allows_valid_dry_run_without_execution_approval()
    {
        var configuration = CreateValidConfiguration();
        configuration.ExecutionEnabled = false;

        var result = KafkaCapacityEnvironmentGuard.ValidatePlan(
            configuration,
            KafkaCapacityOptions.Parse([]));

        Assert.IsTrue(result.IsAllowed, result.Message);
        Assert.AreEqual("allowed", result.ReasonCode);
    }

    [TestMethod]
    [DataRow(false, "Capacity", "approval", "capacity test", "execution_disabled")]
    [DataRow(true, "Production", "approval", "capacity test", "production_forbidden")]
    [DataRow(true, "Capacity", null, "capacity test", "approval_required")]
    [DataRow(true, "Capacity", "approval", null, "reason_required")]
    public void Environment_guard_rejects_unsafe_execution_plan(
        bool executionEnabled,
        string environmentName,
        string? approvalId,
        string? reason,
        string expectedReasonCode)
    {
        var configuration = CreateValidConfiguration();
        configuration.ExecutionEnabled = executionEnabled;
        configuration.EnvironmentName = environmentName;
        var arguments = new List<string> { "--execute", "true" };
        if (approvalId is not null)
        {
            arguments.AddRange(["--approval-id", approvalId]);
        }

        if (reason is not null)
        {
            arguments.AddRange(["--reason", reason]);
        }

        var result = KafkaCapacityEnvironmentGuard.ValidatePlan(
            configuration,
            KafkaCapacityOptions.Parse(arguments));

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual(expectedReasonCode, result.ReasonCode);
    }

    [TestMethod]
    [DataRow(false, "broker:9092", "kafka_disabled")]
    [DataRow(true, "", "bootstrap_servers_required")]
    public void Environment_guard_rejects_missing_Kafka_prerequisites(
        bool kafkaEnabled,
        string bootstrapServers,
        string expectedReasonCode)
    {
        var configuration = CreateValidConfiguration();
        configuration.Kafka.Enabled = kafkaEnabled;
        configuration.Kafka.BootstrapServers = bootstrapServers;

        var result = KafkaCapacityEnvironmentGuard.ValidatePlan(
            configuration,
            KafkaCapacityOptions.Parse([]));

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual(expectedReasonCode, result.ReasonCode);
    }

    [TestMethod]
    public void Environment_guard_rejects_cluster_mismatch_without_leaking_secrets()
    {
        var configuration = CreateValidConfiguration();
        configuration.Kafka.SaslPassword = "must-not-leak";

        var result = KafkaCapacityEnvironmentGuard.ValidateCluster(
            configuration,
            KafkaCapacityOptions.Parse([]),
            new KafkaCapacityClusterIdentity("unexpected", BrokerCount: 3));

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual("cluster_id_mismatch", result.ReasonCode);
        Assert.IsFalse(result.Message.Contains("must-not-leak", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Environment_guard_rejects_replication_factor_above_broker_count()
    {
        var configuration = CreateValidConfiguration();

        var result = KafkaCapacityEnvironmentGuard.ValidateCluster(
            configuration,
            KafkaCapacityOptions.Parse([
                "--replication-factor", "3",
            ]),
            new KafkaCapacityClusterIdentity("expected", BrokerCount: 2));

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual("insufficient_brokers", result.ReasonCode);
    }

    private static KafkaCapacityConfiguration CreateValidConfiguration() =>
        new()
        {
            ExecutionEnabled = true,
            EnvironmentName = "Capacity",
            ExpectedClusterId = "expected",
            Kafka = new KafkaMessagingOptions
            {
                Enabled = true,
                BootstrapServers = "broker:9092",
            },
        };
}
