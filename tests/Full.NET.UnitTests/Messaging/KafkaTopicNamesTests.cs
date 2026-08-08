using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaTopicNamesTests
{
    [TestMethod]
    public void GetRetryTopic_appends_configured_stage_suffix()
    {
        var topic = "fullnet.dev.messaging.inbox-test.v1";

        var retryTopic = KafkaTopicNames.GetRetryTopic(topic, "5s");

        Assert.AreEqual("fullnet.dev.messaging.inbox-test.v1.retry.5s", retryTopic);
    }

    [TestMethod]
    public void ResolveBaseTopic_strips_retry_and_dlq_suffixes()
    {
        Assert.AreEqual(
            "fullnet.dev.messaging.inbox-test.v1",
            KafkaTopicNames.ResolveBaseTopic("fullnet.dev.messaging.inbox-test.v1.retry.1m"));
        Assert.AreEqual(
            "fullnet.dev.messaging.inbox-test.v1",
            KafkaTopicNames.ResolveBaseTopic("fullnet.dev.messaging.inbox-test.v1.dlq"));
    }
}
