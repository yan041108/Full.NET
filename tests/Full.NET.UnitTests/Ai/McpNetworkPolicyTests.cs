using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 MCP：Network Policy。</summary>
[TestClass]
public sealed class McpNetworkPolicyTests
{
    [TestMethod]
    public void Registered_transport_requires_connection_validation()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddFullNetMcpClient(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(McpClientConnectionManager));
        while (handler is DelegatingHandler outer) handler = outer.InnerHandler!;
        var sockets = (SocketsHttpHandler)handler;
        Assert.IsFalse(sockets.AllowAutoRedirect);
        Assert.IsFalse(sockets.UseProxy);
        Assert.IsFalse(sockets.UseCookies);
        Assert.IsNotNull(sockets.ConnectCallback);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("invalid")]
    [DataRow("1")]
    public void Invalid_private_mode_fails_at_registration(string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly"] = value,
        }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddFullNetMcpClient(configuration));
    }

    [TestMethod]
    public async Task Private_mode_blocks_unapproved_public_endpoint_before_factory()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly"] = "true",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFullNetMcpClient(configuration);
        var factory = new ForbiddenFactory();
        services.AddSingleton<IHttpClientFactory>(factory);
        using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => manager.GetClientAsync(
            Guid.CreateVersion7(), new Uri("https://remote.test/mcp"), "service-token", default).AsTask());
        Assert.AreEqual(0, factory.Calls);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("http://host.test/path")]
    [DataRow("http://host.test?x=1")]
    [DataRow("http://host.test#fragment")]
    [DataRow("https://user:secret@host.test")]
    [DataRow("http://8.8.8.8")]
    [DataRow("https://169.254.169.254")]
    public void Invalid_origin_fails_at_registration(string? origin)
    {
        var configuration = Configuration(false, origin);
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddFullNetMcpClient(configuration));
    }

    [TestMethod]
    public void Object_private_flag_fails_closed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly:Enabled"] = "false",
        }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddFullNetMcpClient(configuration));
    }

    [TestMethod]
    public async Task Configuration_reload_cannot_open_private_policy()
    {
        var configuration = Configuration(true, "https://private.test");
        var services = new ServiceCollection().AddLogging();
        services.AddFullNetMcpClient(configuration);
        configuration["FullNet:Ai:Network:PrivateOnly"] = "false";
        configuration["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = "https://public.test";
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpClientOptions>>().Value;
        Assert.IsTrue(options.PrivateOnly);
        CollectionAssert.AreEqual(new[] { "https://private.test" }, options.ApprovedOrigins);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(McpClientConnectionManager));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("https://public.test/mcp"));
    }

    [TestMethod]
    [DataRow("https://public.test/mcp")]
    [DataRow("https://private.test:444/mcp")]
    [DataRow("http://private.test/mcp")]
    [DataRow("https://private.test/mcp#fragment")]
    [DataRow("https://user:secret@private.test/mcp")]
    [DataRow("file:///secret")]
    public void Private_mode_requires_exact_origin(string destination)
    {
        var policy = new McpEndpointPolicy(["https://private.test"], true);
        Assert.ThrowsExactly<HttpRequestException>(() => policy.ValidateEndpoint(new Uri(destination)));
    }

    [TestMethod]
    [DataRow("8.8.8.8")]
    [DataRow("2606:4700:4700::1111")]
    [DataRow("169.254.169.254")]
    [DataRow("fd00:ec2::254")]
    [DataRow("::ffff:8.8.8.8")]
    [DataRow("fe80::1")]
    public async Task Private_dns_denies_whole_result_before_any_connection(string denied)
    {
        var connections = 0;
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.Parse(denied) }),
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("https://private.test/mcp"), new DnsEndPoint("private.test", 443), default).AsTask());
        Assert.AreEqual(0, connections);
    }

    [TestMethod]
    public async Task Unapproved_public_mode_dns_cannot_rebind_to_loopback()
    {
        var connector = new McpNetworkConnector(new([]),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }),
            (_, _) => throw new AssertFailedException("Unsafe address set must not connect."));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("https://remote.test"), new DnsEndPoint("remote.test", 443), default).AsTask());
    }

    [TestMethod]
    public async Task Plaintext_approval_cannot_rebind_to_public_even_without_private_mode()
    {
        var connector = new McpNetworkConnector(new(["http://private.test"]),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            (_, _) => throw new AssertFailedException("Plaintext public request must not connect."));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("http://private.test"), new DnsEndPoint("private.test", 80), default).AsTask());
    }

    [TestMethod]
    [DataRow("10.0.0.1")]
    [DataRow("127.0.0.1")]
    [DataRow("::1")]
    [DataRow("fd12::1")]
    [DataRow("::ffff:127.0.0.1")]
    public async Task Approved_private_dns_connects_only_validated_ip(string address)
    {
        var expected = IPAddress.Parse(address);
        IPEndPoint? actual = null;
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => Task.FromResult(new[] { expected }),
            (endpoint, _) => { actual = endpoint; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        using var stream = await connector.ConnectAsync(new Uri("https://private.test/mcp"), new("private.test", 443), default);
        Assert.AreEqual(expected, actual!.Address);
        Assert.AreEqual(443, actual.Port);
    }

    [TestMethod]
    public async Task Each_new_connection_revalidates_dns()
    {
        var resolutions = 0;
        var connections = 0;
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => Task.FromResult(new[] { ++resolutions == 1 ? IPAddress.Loopback : IPAddress.Parse("8.8.8.8") }),
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        var uri = new Uri("https://private.test/mcp");
        using var first = await connector.ConnectAsync(uri, new("private.test", 443), default);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(uri, new("private.test", 443), default).AsTask());
        Assert.AreEqual(2, resolutions);
        Assert.AreEqual(1, connections);
    }

    [TestMethod]
    [DataRow("other.test", 443)]
    [DataRow("private.test", 444)]
    public async Task Connection_authority_mismatch_is_denied_before_resolution(string host, int port)
    {
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => throw new AssertFailedException("Mismatched authority must not resolve."),
            (_, _) => throw new AssertFailedException("Mismatched authority must not connect."));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("https://private.test/mcp"), new(host, port), default).AsTask());
    }

    [TestMethod]
    public async Task Empty_dns_is_denied()
    {
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => Task.FromResult(Array.Empty<IPAddress>()),
            (_, _) => throw new AssertFailedException("Empty DNS must not connect."));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("https://private.test/mcp"), new("private.test", 443), default).AsTask());
    }

    [TestMethod]
    public async Task Literal_does_not_resolve_again()
    {
        var connector = new McpNetworkConnector(new(["http://127.0.0.1"], true),
            (_, _) => throw new AssertFailedException("Literal must not resolve."),
            (endpoint, _) =>
            {
                Assert.AreEqual(IPAddress.Loopback, endpoint.Address);
                return ValueTask.FromResult<Stream>(new MemoryStream());
            });
        using var stream = await connector.ConnectAsync(new Uri("http://127.0.0.1/mcp"), new("127.0.0.1", 80), default);
        Assert.IsNotNull(stream);
    }

    [TestMethod]
    public async Task Cancelled_dns_does_not_connect()
    {
        using var cancellation = new CancellationTokenSource();
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => { cancellation.Cancel(); return Task.FromResult(new[] { IPAddress.Loopback }); },
            (_, _) => throw new AssertFailedException("Cancelled operation must not connect."));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => connector.ConnectAsync(
            new Uri("https://private.test/mcp"), new("private.test", 443), cancellation.Token).AsTask());
    }

    [TestMethod]
    public async Task Socket_failure_only_tries_remaining_validated_private_ip()
    {
        var attempts = new List<IPAddress>();
        var second = IPAddress.Parse("10.0.0.2");
        var connector = new McpNetworkConnector(new(["https://private.test"], true),
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback, second }),
            (endpoint, _) =>
            {
                attempts.Add(endpoint.Address);
                if (attempts.Count == 1) throw new SocketException((int)SocketError.ConnectionRefused);
                return ValueTask.FromResult<Stream>(new MemoryStream());
            });
        using var stream = await connector.ConnectAsync(new Uri("https://private.test/mcp"), new("private.test", 443), default);
        CollectionAssert.AreEqual(new[] { IPAddress.Loopback, second }, attempts);
    }

    [TestMethod]
    public async Task Public_https_remains_supported_when_private_mode_is_off()
    {
        var connector = new McpNetworkConnector(new([]),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            (_, _) => ValueTask.FromResult<Stream>(new MemoryStream()));
        using var stream = await connector.ConnectAsync(new Uri("https://remote.test"), new("remote.test", 443), default);
        Assert.IsNotNull(stream);
    }

    [TestMethod]
    public async Task Real_socket_redirect_does_not_follow_or_leak_token()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        var responseTask = RespondAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddFullNetMcpClient(Configuration(true, origin));
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(McpClientConnectionManager));
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-token");
        using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/mcp")
        {
            Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        using var response = await client.SendAsync(request, timeout.Token);
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var header = await responseTask;
        StringAssert.StartsWith(header, "POST /mcp HTTP/1.1");
        StringAssert.Contains(header, "Authorization: Bearer synthetic-token");
        Assert.AreEqual(HttpVersion.Version11, request.Version);
        Assert.AreEqual(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
        Assert.IsFalse(listener.Pending());
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("https://unapproved.test/mcp", timeout.Token));

        async Task<string> RespondAsync()
        {
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = accepted.GetStream();
            var bytes = new List<byte>();
            var buffer = new byte[1];
            while (bytes.Count < 8192)
            {
                if (await stream.ReadAsync(buffer, timeout.Token) == 0) throw new IOException("Unexpected EOF.");
                bytes.Add(buffer[0]);
                if (bytes.Count >= 4 && bytes.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
            }
            var header = Encoding.ASCII.GetString(bytes.ToArray());
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: " + origin + "/redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
            return header;
        }
    }

    private static IConfigurationRoot Configuration(bool privateOnly, string? origin) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly"] = privateOnly ? "true" : "false",
            ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = origin,
        }).Build();

    private sealed class ForbiddenFactory : IHttpClientFactory
    {
        internal int Calls { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Calls++;
            throw new InvalidOperationException("Factory must not be reached.");
        }
    }
}
