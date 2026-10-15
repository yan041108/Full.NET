using MessagePack;

namespace Full.NET.UnitTests.Realtime;

/// <summary>Redis 回传层的传递解析器必须阻止嵌套容器重复承诺同一段剩余字节。</summary>
[TestClass]
public sealed class RealtimeMessagePackBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Nested_headers_cannot_reuse_outer_container_byte_budget(bool map)
    {
        Assert.ThrowsExactly<EndOfStreamException>(() =>
        {
            byte[] payload = map ? [0x82, 0x82, 0xc0, 0xc0, 0xc0, 0xc0] : [0x93, 0x94, 0xc0, 0xc0, 0xc0, 0xc0];
            var reader = new MessagePackReader(payload);
            if (map) { Assert.AreEqual(2, reader.ReadMapHeader()); reader.ReadMapHeader(); }
            else { Assert.AreEqual(3, reader.ReadArrayHeader()); reader.ReadArrayHeader(); }
        });
    }

    [TestMethod]
    public void Valid_nested_array_retires_consumed_child_commitments()
    {
        var reader = new MessagePackReader(new byte[] { 0x93, 0x92, 0xc0, 0xc0, 0xc0, 0xc0 });
        Assert.AreEqual(3, reader.ReadArrayHeader());
        Assert.AreEqual(2, reader.ReadArrayHeader());
        for (var index = 0; index < 4; index++) reader.ReadNil();
        Assert.IsTrue(reader.End);
    }
}
