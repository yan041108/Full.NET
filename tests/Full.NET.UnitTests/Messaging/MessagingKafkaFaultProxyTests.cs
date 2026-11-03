using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.UnitTests.Messaging;

/// <summary>复用真实故障代理，核对完整帧字节、提交门控和丢弃语义。</summary>
[TestClass]
public sealed class MessagingKafkaFaultProxyTests
{
    [TestMethod]
    public Task Offset_commit_gate_preserves_request_and_response_bytes() => VerifyAsync(drop: false);

    [TestMethod]
    public Task Dropped_offset_commit_never_reaches_upstream() => VerifyAsync(drop: true);

    private static async Task VerifyAsync(bool drop)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        await using var proxy = new MessagingKafkaFaultProxy
        {
            UpstreamPort = checked((ushort)((IPEndPoint)server.LocalEndpoint).Port),
        };
        var group = "frame-test-" + Guid.NewGuid().ToString("N");
        var gate = proxy.ArmOffsetCommit(group);
        using var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port, timeout.Token);
            var accepted = server.AcceptTcpClientAsync(timeout.Token).AsTask();
            var packet = Request(group);
            await client.GetStream().WriteAsync(packet, timeout.Token);
            await gate.Reached.Task.WaitAsync(timeout.Token);
            Assert.AreEqual("fullnet-frame-test", gate.ClientId);
            using var upstream = await accepted;
            var prefix = new byte[4];
            var reading = upstream.GetStream().ReadExactlyAsync(prefix, timeout.Token).AsTask();
            // Gate 命中后必须实际阻止上游收帧；超时不取消底层读取，释放后继续检查原字节。
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => reading.WaitAsync(TimeSpan.FromMilliseconds(75)));
            gate.Drop = drop; gate.Release.TrySetResult();
            if (drop)
            {
                await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => reading);
                return;
            }
            await reading;
            var actual = new byte[4 + BinaryPrimitives.ReadInt32BigEndian(prefix)];
            prefix.CopyTo(actual, 0);
            await upstream.GetStream().ReadExactlyAsync(actual.AsMemory(4), timeout.Token);
            CollectionAssert.AreEqual(packet, actual);
            var response = new byte[] { 0, 0, 0, 4, 0, 0, 0, 7 };
            await upstream.GetStream().WriteAsync(response, timeout.Token);
            var received = new byte[response.Length];
            await client.GetStream().ReadExactlyAsync(received, timeout.Token);
            CollectionAssert.AreEqual(response, received);
        }
        finally { gate.Release.TrySetResult(); server.Stop(); }
    }

    private static byte[] Request(string group)
    {
        var client = Encoding.UTF8.GetBytes("fullnet-frame-test");
        var groupBytes = Encoding.UTF8.GetBytes(group);
        var packet = new byte[4 + 12 + client.Length + groupBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length - 4);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(4), 8);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8), 7);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(12), checked((short)client.Length));
        client.CopyTo(packet, 14);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(14 + client.Length), checked((short)groupBytes.Length));
        groupBytes.CopyTo(packet, 16 + client.Length);
        return packet;
    }
}
