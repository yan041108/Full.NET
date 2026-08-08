using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaFailureClassifierTests
{
    [TestMethod]
    public void Classify_maps_permanent_exception_to_failure_kind()
    {
        var failure = new IntegrationEventFailure(
            IntegrationEventFailureKind.Contract,
            IntegrationEventFailureCodes.PayloadRequired,
            "Payload required.");
        var classifier = new KafkaFailureClassifier();

        var classified = classifier.Classify(new IntegrationEventPermanentException(failure));

        Assert.AreEqual(IntegrationEventFailureKind.Contract, classified.Kind);
        Assert.AreEqual(IntegrationEventFailureCodes.PayloadRequired, classified.Code);
    }

    [TestMethod]
    public void Classify_maps_io_exception_to_transient_failure()
    {
        var classifier = new KafkaFailureClassifier();

        var classified = classifier.Classify(new IOException("network down"));

        Assert.AreEqual(IntegrationEventFailureKind.Transient, classified.Kind);
        StringAssert.StartsWith(classified.Code, IntegrationEventFailureCodes.TransientPrefix);
    }

    [TestMethod]
    public void ShouldRetry_returns_true_only_for_transient_failures()
    {
        var classifier = new KafkaFailureClassifier();
        var transient = new IntegrationEventFailure(
            IntegrationEventFailureKind.Transient,
            IntegrationEventFailureCodes.TransientPrefix + "broker_or_io",
            "Transient.");
        var contract = new IntegrationEventFailure(
            IntegrationEventFailureKind.Contract,
            IntegrationEventFailureCodes.PayloadRequired,
            "Contract.");

        Assert.IsTrue(classifier.ShouldRetry(transient));
        Assert.IsFalse(classifier.ShouldRetry(contract));
    }
}
