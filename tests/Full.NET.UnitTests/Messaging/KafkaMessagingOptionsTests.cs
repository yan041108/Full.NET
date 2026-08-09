using Confluent.Kafka;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaMessagingOptionsTests
{
    [TestMethod]
    public void BuildConsumerConfig_disables_auto_commit()
    {
        var options = CreateValidDevelopmentOptions();
        var config = options.BuildConsumerConfig("fullnet.messaging.test");

        Assert.IsFalse(config.EnableAutoCommit);
        Assert.IsFalse(config.EnableAutoOffsetStore);
        Assert.AreEqual(1, config.QueuedMinMessages);
        Assert.IsTrue(config.QueuedMaxMessagesKbytes > 0);
    }

    [TestMethod]
    public void BuildConsumerConfig_uses_cooperative_static_membership_for_classic_protocol()
    {
        var options = CreateValidDevelopmentOptions();
        options.ConsumerGroupProtocol = KafkaConsumerGroupProtocolMode.Classic;
        options.ClassicPartitionAssignment = KafkaClassicPartitionAssignmentMode.CooperativeSticky;
        options.CooperativeStickyMigrationCompleted = true;

        var config = options.BuildConsumerConfig("fullnet.messaging.test");

        Assert.AreEqual(GroupProtocol.Classic, config.GroupProtocol);
        Assert.AreEqual(
            PartitionAssignmentStrategy.CooperativeSticky,
            config.PartitionAssignmentStrategy);
        Assert.AreEqual("fullnet.messaging.test-01", config.GroupInstanceId);
    }

    [TestMethod]
    public void BuildConsumerConfig_keeps_legacy_range_assignor_until_offline_migration_is_attested()
    {
        var options = CreateValidDevelopmentOptions();

        var config = options.BuildConsumerConfig("fullnet.messaging.test");

        Assert.AreEqual(PartitionAssignmentStrategy.Range, config.PartitionAssignmentStrategy);
    }

    [TestMethod]
    public void BuildConsumerConfig_consumer_protocol_removes_classic_only_settings()
    {
        var options = CreateValidDevelopmentOptions();
        options.ConsumerGroupProtocol = KafkaConsumerGroupProtocolMode.Consumer;
        options.BrokerMajorVersion = 4;

        var config = options.BuildConsumerConfig("fullnet.messaging.test");

        Assert.AreEqual(GroupProtocol.Consumer, config.GroupProtocol);
        Assert.IsNull(config.PartitionAssignmentStrategy);
        Assert.IsNull(config.SessionTimeoutMs);
    }

    [TestMethod]
    public void Validation_rejects_heartbeat_poll_interval_outside_consumer_bounds()
    {
        var options = CreateValidDevelopmentOptions();
        options.HandlerHeartbeatMilliseconds = options.SessionTimeoutMilliseconds;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "HandlerHeartbeatMilliseconds");
    }

    [TestMethod]
    public void Validation_rejects_retry_stages_that_do_not_increase()
    {
        var options = CreateValidDevelopmentOptions();
        options.RetryStages = ["1m", "5s", "1m"];

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "strictly increasing");
    }

    [TestMethod]
    public void Validation_rejects_ownership_revoked_hot_loop_backoff()
    {
        var options = CreateValidDevelopmentOptions();
        options.OwnershipRevokedBackoffMilliseconds = 100;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "OwnershipRevokedBackoffMilliseconds");
    }

    [TestMethod]
    public void BuildProducerConfig_uses_acks_all_and_idempotence()
    {
        var options = CreateValidDevelopmentOptions();
        var config = options.BuildProducerConfig();

        Assert.AreEqual(Acks.All, config.Acks);
        Assert.IsTrue(config.EnableIdempotence);
    }

    [TestMethod]
    public void BuildProducerConfig_applies_bounded_batching_without_weakening_idempotence()
    {
        var options = CreateValidDevelopmentOptions();
        options.ProducerLingerMilliseconds = 5;
        options.ProducerBatchSizeBytes = 65_536;
        options.ProducerQueueMaxMessages = 20_000;
        options.ProducerQueueMaxKbytes = 65_536;
        options.ProducerMaxInFlightRequests = 5;

        var config = options.BuildProducerConfig();

        Assert.AreEqual(5, config.LingerMs);
        Assert.AreEqual(65_536, config.BatchSize);
        Assert.AreEqual(20_000, config.QueueBufferingMaxMessages);
        Assert.AreEqual(65_536, config.QueueBufferingMaxKbytes);
        Assert.AreEqual(5, config.MaxInFlight);
        Assert.IsTrue(config.EnableIdempotence);
        Assert.AreEqual(Acks.All, config.Acks);
    }

    [TestMethod]
    public void Validation_rejects_consumer_protocol_without_kafka_4_compatibility()
    {
        var options = CreateValidDevelopmentOptions();
        options.ConsumerGroupProtocol = KafkaConsumerGroupProtocolMode.Consumer;
        options.BrokerMajorVersion = 3;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "BrokerMajorVersion");
    }

    [TestMethod]
    public void Validation_rejects_unknown_consumer_group_protocol()
    {
        var options = CreateValidDevelopmentOptions();
        options.ConsumerGroupProtocol = (KafkaConsumerGroupProtocolMode)99;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "ConsumerGroupProtocol");
    }

    [TestMethod]
    public void Validation_rejects_cooperative_sticky_before_offline_migration_is_attested()
    {
        var options = CreateValidDevelopmentOptions();
        options.ClassicPartitionAssignment = KafkaClassicPartitionAssignmentMode.CooperativeSticky;
        options.CooperativeStickyMigrationCompleted = false;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "CooperativeStickyMigrationCompleted");
    }

    [TestMethod]
    public void Validation_rejects_producer_batch_larger_than_one_mebibyte()
    {
        var options = CreateValidDevelopmentOptions();
        options.ProducerBatchSizeBytes = KafkaMessagingOptions.MaxProducerBatchSizeBytes + 1;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "ProducerBatchSizeBytes");
    }

    [TestMethod]
    public void Validation_rejects_idempotent_producer_with_more_than_five_inflight_requests()
    {
        var options = CreateValidDevelopmentOptions();
        options.ProducerMaxInFlightRequests = 6;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "ProducerMaxInFlightRequests");
    }

    [TestMethod]
    public void Validation_requires_at_least_one_mebibyte_of_producer_queue_memory()
    {
        var options = CreateValidDevelopmentOptions();
        options.ProducerQueueMaxKbytes = 1_023;

        var rejected = KafkaMessagingOptionsValidation.Validate(options, "Development");
        Assert.IsFalse(rejected.Succeeded);
        StringAssert.Contains(rejected.FailureMessage, "ProducerQueueMaxKbytes");

        options.ProducerQueueMaxKbytes = 1_024;
        var accepted = KafkaMessagingOptionsValidation.Validate(options, "Development");
        Assert.IsTrue(accepted.Succeeded);
    }

    [TestMethod]
    public void Validation_rejects_invalid_consumer_buffer_hysteresis()
    {
        var options = CreateValidDevelopmentOptions();
        options.ConsumerBufferHighWatermark = 100;
        options.ConsumerBufferLowWatermark = 100;
        options.PartitionBufferHighWatermark = 10;
        options.PartitionBufferLowWatermark = 10;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "ConsumerBufferLowWatermark");
        StringAssert.Contains(result.FailureMessage, "PartitionBufferLowWatermark");
    }

    [TestMethod]
    public void Validation_rejects_key_slot_count_larger_than_partition_buffer()
    {
        var options = CreateValidDevelopmentOptions();
        options.PartitionBufferHighWatermark = 2;
        options.PartitionBufferLowWatermark = 1;
        options.PartitionKeyConcurrencySlots = 3;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "PartitionKeyConcurrencySlots");
    }

    [TestMethod]
    public void Validation_rejects_unverified_periodic_offset_commit_in_production()
    {
        var options = CreateValidDevelopmentOptions();
        options.OffsetCommitMode = KafkaOffsetCommitMode.PeriodicWatermark;
        options.PeriodicOffsetCommitVerified = false;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Production");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "PeriodicOffsetCommitVerified");
    }

    [TestMethod]
    public void Validation_accepts_bounded_periodic_offset_commit_in_development()
    {
        var options = CreateValidDevelopmentOptions();
        options.OffsetCommitMode = KafkaOffsetCommitMode.PeriodicWatermark;
        options.OffsetCommitIntervalMilliseconds = 500;
        options.OffsetCommitBatchSize = 200;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validation_rejects_enable_auto_commit_when_enabled()
    {
        var options = CreateValidDevelopmentOptions();
        options.EnableAutoCommit = true;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "EnableAutoCommit");
    }

    [TestMethod]
    public void Validation_rejects_non_all_acks_when_enabled()
    {
        var options = CreateValidDevelopmentOptions();
        options.Acks = "1";

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Acks");
    }

    [TestMethod]
    public void Validation_rejects_disabled_idempotence_when_enabled()
    {
        var options = CreateValidDevelopmentOptions();
        options.EnableIdempotence = false;

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "EnableIdempotence");
    }

    [TestMethod]
    public void Production_validation_fails_closed_when_enabled_without_security_baseline()
    {
        var options = new KafkaMessagingOptions
        {
            Enabled = true,
            SecurityProtocol = "Plaintext",
            RetryStages = ["5s", "1m", "15m"],
        };

        var result = KafkaMessagingOptionsValidation.Validate(options, "Production");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue((result.Failures ?? []).Any(f => f.Contains("BootstrapServers", StringComparison.Ordinal)));
        Assert.IsTrue((result.Failures ?? []).Any(f => f.Contains("ClientId", StringComparison.Ordinal)));
        Assert.IsTrue((result.Failures ?? []).Any(f => f.Contains("ConsumerInstanceId", StringComparison.Ordinal)));
        Assert.IsTrue((result.Failures ?? []).Any(f => f.Contains("SecurityProtocol", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validation_rejects_message_size_outside_bounds()
    {
        var options = CreateValidDevelopmentOptions();
        options.MessageMaxBytes = KafkaMessagingOptions.MinMessageMaxBytes - 1;

        var underflow = KafkaMessagingOptionsValidation.Validate(options, "Development");
        Assert.IsFalse(underflow.Succeeded);
        StringAssert.Contains(underflow.FailureMessage, "MessageMaxBytes");

        options.MessageMaxBytes = KafkaMessagingOptions.MaxMessageMaxBytes + 1;
        var overflow = KafkaMessagingOptionsValidation.Validate(options, "Development");
        Assert.IsFalse(overflow.Succeeded);
        StringAssert.Contains(overflow.FailureMessage, "MessageMaxBytes");
    }

    [TestMethod]
    public void ToString_redacts_sasl_password()
    {
        var options = CreateValidDevelopmentOptions();
        options.SaslPassword = "super-secret-password";

        var text = options.ToString();

        Assert.Contains("SaslPassword=***", text);
        Assert.DoesNotContain("super-secret-password", text);
    }

    private static KafkaMessagingOptions CreateValidDevelopmentOptions() =>
        new()
        {
            Enabled = true,
            BootstrapServers = "localhost:9092",
            ClientId = "fullnet.messaging.test",
            ConsumerInstanceId = "fullnet.messaging.test-01",
            SecurityProtocol = "Plaintext",
            MessageMaxBytes = 1_048_576,
            RetryStages = ["5s", "1m", "15m"],
        };
}
