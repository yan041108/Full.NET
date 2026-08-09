using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaReplayOperationsOptionsTests
{
    [TestMethod]
    public void Disabled_replay_does_not_require_broker_configuration()
    {
        var validator = new KafkaReplayOperationsOptionsValidator(
            Options.Create(new KafkaMessagingOptions()),
            CreateEnvironment("Testing"));

        var result = validator.Validate(
            null,
            new KafkaReplayOperationsOptions { Enabled = false });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Enabled_replay_requires_broker_and_bounded_http_execution()
    {
        var validator = new KafkaReplayOperationsOptionsValidator(
            Options.Create(new KafkaMessagingOptions()),
            CreateEnvironment("Testing"));

        var result = validator.Validate(
            null,
            new KafkaReplayOperationsOptions
            {
                Enabled = true,
                MaximumSynchronousMessages = 1_001,
                ExecutionTimeoutSeconds = 56,
            });

        Assert.IsTrue(result.Failed);
        var failures = result.Failures?.ToArray() ?? [];
        Assert.IsTrue(failures.Any(item => item.Contains("BootstrapServers", StringComparison.Ordinal)));
        Assert.IsTrue(failures.Any(item => item.Contains("ClientId", StringComparison.Ordinal)));
        Assert.IsTrue(failures.Any(item => item.Contains("MaximumSynchronousMessages", StringComparison.Ordinal)));
        Assert.IsTrue(failures.Any(item => item.Contains("ExecutionTimeoutSeconds", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Production_replay_rejects_plaintext_broker_transport()
    {
        var validator = new KafkaReplayOperationsOptionsValidator(
            Options.Create(new KafkaMessagingOptions
            {
                BootstrapServers = "kafka:9092",
                ClientId = "fullnet-api-replay",
                SecurityProtocol = "Plaintext",
            }),
            CreateEnvironment(Environments.Production));

        var result = validator.Validate(
            null,
            new KafkaReplayOperationsOptions { Enabled = true });

        Assert.IsTrue(result.Failed);
        Assert.IsTrue((result.Failures ?? []).Any(item =>
            item.Contains("must use TLS", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Enabled_sasl_replay_rejects_unknown_mechanism_during_startup_validation()
    {
        var validator = new KafkaReplayOperationsOptionsValidator(
            Options.Create(new KafkaMessagingOptions
            {
                BootstrapServers = "kafka:9093",
                ClientId = "fullnet-api-replay",
                SecurityProtocol = "SaslSsl",
                SaslMechanism = "ScramSha999",
                SaslUsername = "replay-user",
                SaslPassword = "replay-password",
            }),
            CreateEnvironment(Environments.Production));

        var result = validator.Validate(
            null,
            new KafkaReplayOperationsOptions { Enabled = true });

        Assert.IsTrue(result.Failed);
        Assert.IsTrue((result.Failures ?? []).Any(item =>
            item.Contains("SaslMechanism is not supported", StringComparison.Ordinal)));
    }

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return environment;
    }
}
