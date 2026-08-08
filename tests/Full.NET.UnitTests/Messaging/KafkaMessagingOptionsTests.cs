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