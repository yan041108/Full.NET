using Full.NET.AI.Providers.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiProviderEndpointPolicyTests
{
    [TestMethod]
    [DataRow("ollama", true)]
    [DataRow("ollama", false)]
    [DataRow("openai_compatible", true)]
    [DataRow("openai_compatible", false)]
    public void Provider_transport_does_not_follow_redirects_or_use_system_proxy(string key, bool connectivity)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiProviderHttpClients(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(AiProviderHttpClients.Name(key, connectivity));
        while (handler is DelegatingHandler outer) handler = outer.InnerHandler!;
        var sockets = (SocketsHttpHandler)handler;
        Assert.IsFalse(sockets.AllowAutoRedirect);
        Assert.IsFalse(sockets.UseProxy);
        Assert.IsFalse(sockets.UseCookies);
        Assert.IsNotNull(sockets.ConnectCallback);
    }

    [TestMethod]
    [DataRow("169.254.169.254")]
    [DataRow("168.63.129.16")]
    [DataRow("100.100.100.200")]
    [DataRow("0.0.0.0")]
    [DataRow("224.0.0.1")]
    [DataRow("240.0.0.1")]
    [DataRow("192.0.2.1")]
    [DataRow("::")]
    [DataRow("fe80::1")]
    [DataRow("ff02::1")]
    [DataRow("::ffff:169.254.169.254")]
    [DataRow("64:ff9b::a9fe:a9fe")]
    [DataRow("2002:a9fe:a9fe::1")]
    [DataRow("fd00:ec2::254")]
    public void Reserved_and_metadata_addresses_are_never_exempted(string address)
    {
        Assert.IsFalse(AiEndpointPolicy.IsAllowedAddress(IPAddress.Parse(address), false));
        Assert.IsFalse(AiEndpointPolicy.IsAllowedAddress(IPAddress.Parse(address), true));
    }

    [TestMethod]
    [DataRow("10.0.0.1")]
    [DataRow("172.16.0.1")]
    [DataRow("192.168.0.1")]
    [DataRow("127.0.0.1")]
    [DataRow("::1")]
    [DataRow("fd12::1")]
    [DataRow("::ffff:127.0.0.1")]
    public void Private_or_loopback_addresses_require_exact_origin_approval(string address)
    {
        Assert.IsFalse(AiEndpointPolicy.IsAllowedAddress(IPAddress.Parse(address), false));
        Assert.IsTrue(AiEndpointPolicy.IsAllowedAddress(IPAddress.Parse(address), true));
    }

    [TestMethod]
    [DataRow("8.8.8.8")]
    [DataRow("2606:4700:4700::1111")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.IsTrue(AiEndpointPolicy.IsAllowedAddress(IPAddress.Parse(address), false));

    [TestMethod]
    [DataRow("http://ollama.test:11434", false)]
    [DataRow("http://ollama.test:11435", true)]
    [DataRow("http://other.test:11434", true)]
    [DataRow("https://user:secret@ollama.test", true)]
    [DataRow("file:///etc/passwd", true)]
    public void Approval_is_scoped_to_provider_scheme_host_and_port(string destination, bool ollama)
    {
        var policy = new AiEndpointPolicy(["http://ollama.test:11434"]);
        Assert.ThrowsExactly<HttpRequestException>(() => policy.ValidateUri(new Uri(destination), ollama));
    }

    [TestMethod]
    [DataRow("http://169.254.169.254")]
    [DataRow("http://[fd00:ec2::254]")]
    [DataRow("http://host.test/path")]
    [DataRow("http://host.test?query=1")]
    public void Invalid_allowlist_fails_during_host_configuration(string origin) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => new AiEndpointPolicy([origin]));

    [TestMethod]
    public async Task Mixed_dns_result_is_rejected_before_any_connection()
    {
        var connections = 0;
        var connector = new AiNetworkConnector(new([]), false,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }),
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("https://provider.test"), new DnsEndPoint("provider.test", 443), default).AsTask());
        Assert.AreEqual(0, connections);
    }

    [TestMethod]
    public async Task Approved_http_origin_cannot_rebind_to_public_addresses()
    {
        var connections = 0;
        var connector = new AiNetworkConnector(new(["http://ollama.test"]), true,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("http://ollama.test"), new DnsEndPoint("ollama.test", 80), default).AsTask());
        Assert.AreEqual(0, connections);
    }

    [TestMethod]
    public async Task New_connections_revalidate_dns_and_connect_only_the_validated_ip()
    {
        var resolutions = 0;
        var destinations = new List<IPAddress>();
        var connector = new AiNetworkConnector(new([]), false,
            (_, _) => Task.FromResult(new[] { ++resolutions == 1 ? IPAddress.Parse("8.8.8.8") : IPAddress.Loopback }),
            (endpoint, _) => { destinations.Add(endpoint.Address); return ValueTask.FromResult<Stream>(new MemoryStream()); });
        var uri = new Uri("https://provider.test");
        var endpoint = new DnsEndPoint("provider.test", 443);
        using var stream = await connector.ConnectAsync(uri, endpoint, default);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(uri, endpoint, default).AsTask());
        Assert.AreEqual(2, resolutions);
        CollectionAssert.AreEqual(new[] { IPAddress.Parse("8.8.8.8") }, destinations);
    }

    [TestMethod]
    public async Task Cancellation_during_resolution_never_connects()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0;
        var connector = new AiNetworkConnector(new([]), false,
            async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return []; },
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        var pending = connector.ConnectAsync(new Uri("https://provider.test"), new DnsEndPoint("provider.test", 443), cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(0, connections);
    }

    [TestMethod]
    public async Task Approved_loopback_uses_real_socket_but_does_not_follow_redirect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var serving = ServeAsync();
        using var handler = AiProviderHttpClients.CreateHandler(new([origin]), true);
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(origin + "/api/tags", deadline.Token);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        await serving;

        async Task ServeAsync()
        {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
        }
    }
}
