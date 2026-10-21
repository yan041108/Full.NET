using System.Net;
using Full.NET.Modules.Webhooks.Delivery;

namespace Full.NET.UnitTests.Webhooks;

/// <summary>连接边界必须验证实际 IP；整组拒绝混入私网的解析结果。</summary>
[TestClass]
public sealed class WebhookNetworkConnectorTests
{
    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("10.0.0.1")]
    [DataRow("169.254.169.254")]
    [DataRow("::1")]
    [DataRow("::ffff:127.0.0.1")]
    [DataRow("fd00::1")]
    public async Task Private_or_mixed_dns_never_reaches_connect(string blocked)
    {
        var connected = false;
        var connector = new WebhookNetworkConnector((_, _) => Task.FromResult(new[] {
            IPAddress.Parse("93.184.216.34"), IPAddress.Parse(blocked) }), (_, _) =>
            { connected = true; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await connector.ConnectAsync(new Uri("https://hooks.example.com"), new DnsEndPoint("hooks.example.com", 443), default));
        Assert.IsFalse(connected);
    }

    [TestMethod]
    [DataRow("93.184.216.34")]
    [DataRow("2606:4700:4700::1111")]
    public async Task Connect_uses_the_validated_ip_without_a_second_lookup(string address)
    {
        var resolutions = 0;
        IPEndPoint? destination = null;
        var connector = new WebhookNetworkConnector((_, _) =>
            { resolutions++; return Task.FromResult(new[] { IPAddress.Parse(address) }); }, (target, _) =>
            { destination = target; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await using var stream = await connector.ConnectAsync(new Uri("https://hooks.example.com:8443"),
            new DnsEndPoint("hooks.example.com", 8443), default);
        Assert.AreEqual(1, resolutions);
        Assert.AreEqual(IPAddress.Parse(address), destination!.Address);
        Assert.AreEqual(8443, destination.Port);
    }

    [TestMethod]
    public async Task Host_mismatch_fails_before_dns_or_socket()
    {
        var connector = new WebhookNetworkConnector((_, _) => throw new AssertFailedException(),
            (_, _) => throw new AssertFailedException());
        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await connector.ConnectAsync(new Uri("https://hooks.example.com"), new DnsEndPoint("internal.example.com", 443), default));
    }
}
