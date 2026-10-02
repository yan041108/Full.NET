using Confluent.Kafka;
using Full.NET.Logging.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class KafkaLogProducerOptionsTests
{
    [TestMethod]
    public void Valid_tls_configuration_builds_bounded_idempotent_producer()
    {
        var options = ValidOptions();
        options.SslCaLocation = "/var/run/fullnet/logging/kafka-ca/ca.crt";
        var config = options.BuildProducerConfig();

        Assert.AreEqual(Acks.All, config.Acks);
        Assert.AreEqual(true, config.EnableIdempotence);
        Assert.AreEqual(true, config.EnableDeliveryReports);
        Assert.AreEqual("none", config.DeliveryReportFields);
        Assert.AreEqual(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.AreEqual(SaslMechanism.ScramSha256, config.SaslMechanism);
        Assert.AreEqual(5, config.MaxInFlight);
        Assert.AreEqual(10_000, config.QueueBufferingMaxMessages);
        Assert.AreEqual(65_536, config.QueueBufferingMaxKbytes);
        Assert.AreEqual(30_000, config.MessageTimeoutMs);
        Assert.AreEqual(options.SslCaLocation, config.SslCaLocation);
    }

    [TestMethod]
    public void Invalid_or_missing_connection_topics_and_limits_fail_without_echoing_secrets()
    {
        var options = ValidOptions();
        options.BootstrapServers = "user:password@broker:9093";
        options.PriorityTopic = options.GeneralTopic;
        options.MaxPendingMessages = 0;
        options.MaxInFlightRequests = 6;

        var result = new KafkaLogProducerOptionsValidator().Validate(null, options);
        Assert.IsFalse(result.Succeeded);
        var errors = string.Join(" ", result.Failures!);
        StringAssert.Contains(errors, "BootstrapServers");
        StringAssert.Contains(errors, "PriorityTopic");
        StringAssert.Contains(errors, "MaxPendingMessages");
        StringAssert.Contains(errors, "MaxInFlightRequests");
        Assert.IsFalse(errors.Contains("password", StringComparison.Ordinal));
        Assert.IsFalse(options.ToString().Contains(options.SaslPassword!, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Plaintext_and_incomplete_sasl_credentials_are_rejected()
    {
        var options = ValidOptions();
        options.SecurityProtocol = "Plaintext";
        Assert.IsFalse(new KafkaLogProducerOptionsValidator().Validate(null, options).Succeeded);

        options.SecurityProtocol = "SaslSsl";
        options.SaslPassword = null;
        Assert.IsFalse(new KafkaLogProducerOptionsValidator().Validate(null, options).Succeeded);
    }

    [TestMethod]
    public void Message_larger_than_configured_application_budget_is_rejected_before_sdk_creation()
    {
        var options = ValidOptions();
        options.MaxPendingBytes = 1_024;

        var error = Assert.ThrowsExactly<OptionsValidationException>(() => options.BuildProducerConfig());
        StringAssert.Contains(error.Message, "MaxPendingBytes");
        Assert.IsFalse(error.Message.Contains(options.SaslPassword!, StringComparison.Ordinal));
    }

    private static KafkaLogProducerOptions ValidOptions() => new()
    {
        BootstrapServers = "broker-a:9093,broker-b:9093",
        GeneralTopic = "fullnet.logs.b2",
        PriorityTopic = "fullnet.logs.priority",
        SecurityProtocol = "SaslSsl",
        SaslMechanism = "ScramSha256",
        SaslUsername = "log-writer",
        SaslPassword = "secret-value",
    };
}
