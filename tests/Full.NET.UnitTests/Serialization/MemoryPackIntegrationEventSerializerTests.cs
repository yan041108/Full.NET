using Full.NET.Serialization.MemoryPack;
using global::MemoryPack;

namespace Full.NET.UnitTests.Serialization;

[TestClass]
public sealed partial class MemoryPackIntegrationEventSerializerTests
{
    [TestMethod]
    public void Serializer_round_trips_an_explicit_contract()
    {
        var serializer = new MemoryPackIntegrationEventSerializer();
        var expected = new TestIntegrationEvent(Guid.CreateVersion7(), "acme");

        var payload = serializer.Serialize(expected);
        var actual = serializer.Deserialize<TestIntegrationEvent>(payload);

        Assert.AreEqual("application/x-memorypack", serializer.ContentType);
        Assert.IsTrue(payload.Length > 0);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Serializer_produces_deterministic_bytes_for_equivalent_payloads()
    {
        var serializer = new MemoryPackIntegrationEventSerializer();
        var tenantId = Guid.CreateVersion7();
        var first = serializer.Serialize(new TestIntegrationEvent(tenantId, "acme"));
        var second = serializer.Serialize(new TestIntegrationEvent(tenantId, "acme"));

        CollectionAssert.AreEqual(first, second);
    }

    [MemoryPackable]
    public partial record TestIntegrationEvent(Guid Id, string Identifier);
}
