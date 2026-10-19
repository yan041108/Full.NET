using Full.NET.Serialization.MemoryPack;
using global::MemoryPack;

namespace Full.NET.UnitTests.Serialization;

/// <summary>单元测试（Serialization）：Memory Pack Integration Event Serializer。</summary>
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

    /// <summary>测试用集成事件契约；字段顺序与 MemoryPack 线格式绑定。</summary>
    [MemoryPackable]
    public partial record TestIntegrationEvent(Guid Id, string Identifier);
}
