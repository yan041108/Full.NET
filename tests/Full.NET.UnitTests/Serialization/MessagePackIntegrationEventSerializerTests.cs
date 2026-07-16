using Full.NET.Serialization.MessagePack;
using MessagePack;

namespace Full.NET.UnitTests.Serialization;

[TestClass]
public sealed class MessagePackIntegrationEventSerializerTests
{
    [TestMethod]
    public void Serializer_round_trips_an_explicit_integer_key_contract()
    {
        var serializer = new MessagePackIntegrationEventSerializer();
        var expected = new TestIntegrationEvent(Guid.CreateVersion7(), "acme");

        var payload = serializer.Serialize(expected);
        var actual = serializer.Deserialize<TestIntegrationEvent>(payload);

        Assert.AreEqual("application/x-msgpack", serializer.ContentType);
        Assert.IsTrue(payload.Length > 0);
        Assert.AreEqual(expected, actual);
    }

    [MessagePackObject]
    public sealed record TestIntegrationEvent(
        [property: Key(0)] Guid Id,
        [property: Key(1)] string Identifier);
}
