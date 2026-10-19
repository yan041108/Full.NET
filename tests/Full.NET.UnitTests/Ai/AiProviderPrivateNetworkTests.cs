using System.Net;
using System.Net.Sockets;
using System.Text;
using Full.NET.AI.Providers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Provider Private Network。</summary>
[TestClass]
public sealed class AiProviderPrivateNetworkTests
{
    [TestMethod]
    [DataRow("ollama", false, false)]
    [DataRow("ollama", false, true)]
    [DataRow("ollama", true, false)]
    [DataRow("openai_compatible", false, false)]
    [DataRow("openai_compatible", false, true)]
    [DataRow("openai_compatible", true, false)]
    [DataRow("azure_openai", false, false)]
    [DataRow("azure_openai", false, true)]
    [DataRow("azure_openai", true, false)]
    public async Task Private_only_blocks_public_destinations_for_every_registered_client(string key, bool connectivity, bool embedding)
    {
        using var provider = Services("true").BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderHttpClients.Name(key, connectivity, embedding));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("https://8.8.8.8/", deadline.Token));
        Assert.AreEqual("AI destination is blocked by host network policy.", error.Message);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("yes")]
    [DataRow("1")]
    [DataRow("tru")]
    public void Invalid_private_only_configuration_fails_at_registration(string? value) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Services(value));

    [TestMethod]
    public void Object_instead_of_private_only_boolean_fails_at_registration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly:Enabled"] = "true",
        }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddAiProviderHttpClients(configuration));
    }

    [TestMethod]
    [DataRow("https://unapproved.test", true)]
    [DataRow("https://ollama.test:444", true)]
    [DataRow("http://ollama.test", true)]
    [DataRow("https://ollama.test", false)]
    [DataRow("https://8.8.8.8", true)]
    [DataRow("https://[2606:4700:4700::1111]", true)]
    public void Private_only_requires_exact_approved_ollama_origin(string destination, bool ollama)
    {
        var policy = new AiEndpointPolicy(["https://ollama.test"], true);
        Assert.ThrowsExactly<HttpRequestException>(() => policy.ValidateUri(new Uri(destination), ollama));
    }

    [TestMethod]
    [DataRow("https://8.8.8.8")]
    [DataRow("https://[2606:4700:4700::1111]")]
    public void Private_only_rejects_public_literal_allowlist_during_registration(string origin) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => new AiEndpointPolicy([origin], true));

    [TestMethod]
    [DataRow("8.8.8.8", "8.8.4.4")]
    [DataRow("127.0.0.1", "8.8.8.8")]
    [DataRow("fd12::1", "2606:4700:4700::1111")]
    public async Task Private_only_rejects_entire_https_dns_group_before_connecting(string first, string second)
    {
        var connections = 0;
        var connector = new AiNetworkConnector(new(["https://ollama.test"], true), true,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(first), IPAddress.Parse(second) }),
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => connector.ConnectAsync(
            new Uri("https://ollama.test"), new DnsEndPoint("ollama.test", 443), default).AsTask());
        Assert.AreEqual(0, connections);
    }

    [TestMethod]
    [DataRow("10.0.0.1")]
    [DataRow("127.0.0.1")]
    [DataRow("fd12::1")]
    [DataRow("::ffff:127.0.0.1")]
    public async Task Private_only_connects_to_validated_private_https_ip(string address)
    {
        var destinations = new List<IPAddress>();
        var connector = new AiNetworkConnector(new(["https://ollama.test"], true), true,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(address) }),
            (endpoint, _) => { destinations.Add(endpoint.Address); return ValueTask.FromResult<Stream>(new MemoryStream()); });
        using var stream = await connector.ConnectAsync(new Uri("https://ollama.test"), new DnsEndPoint("ollama.test", 443), default);
        CollectionAssert.AreEqual(new[] { IPAddress.Parse(address) }, destinations);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task Private_only_registered_ollama_clients_use_real_approved_socket(bool connectivity, bool embedding)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly"] = "true",
            ["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = origin,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiProviderHttpClients(configuration);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderHttpClients.Name("ollama", connectivity, embedding));
        var serving = ServeAsync();
        using var content = new StringContent("private content", Encoding.UTF8);
        using var response = await client.PostAsync(origin + "/api/test", content, deadline.Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        await serving;

        async Task ServeAsync()
        {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            Assert.AreEqual("POST /api/test HTTP/1.1", await reader.ReadLineAsync(deadline.Token));
            var length = 0;
            while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } line)
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(15, length);
            var body = new char[length];
            Assert.AreEqual(length, await reader.ReadBlockAsync(body.AsMemory(), deadline.Token));
            Assert.AreEqual("private content", new string(body));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
        }
    }

    [TestMethod]
    public async Task Private_only_snapshot_cannot_be_disabled_by_configuration_reload()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiProviderHttpClients(configuration);
        configuration["FullNet:Ai:Network:PrivateOnly"] = "false";
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderHttpClients.Name("ollama", false));
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("https://8.8.8.8/"));
        Assert.AreEqual("AI destination is blocked by host network policy.", error.Message);
    }

    [TestMethod]
    public async Task Private_connection_failure_only_tries_remaining_validated_private_addresses()
    {
        var destinations = new List<IPAddress>();
        var connector = new AiNetworkConnector(new(["https://ollama.test"], true), true,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2") }),
            (endpoint, _) =>
            {
                destinations.Add(endpoint.Address);
                if (destinations.Count == 1) throw new SocketException();
                return ValueTask.FromResult<Stream>(new MemoryStream());
            });
        using var stream = await connector.ConnectAsync(new Uri("https://ollama.test"), new DnsEndPoint("ollama.test", 443), default);
        CollectionAssert.AreEqual(new[] { IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2") }, destinations);
    }

    [TestMethod]
    public void Default_and_explicit_false_preserve_existing_public_https_policy()
    {
        Assert.IsFalse(new AiEndpointPolicy([]).ValidateUri(new Uri("https://8.8.8.8"), true));
        Assert.IsFalse(new AiEndpointPolicy([], false).ValidateUri(new Uri("https://8.8.8.8"), false));
    }

    private static ServiceCollection Services(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:Network:PrivateOnly"] = value,
            ["FullNet:Ai:Network:OllamaAllowedOrigins:0"] = "http://127.0.0.1:11434",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiProviderHttpClients(configuration);
        return services;
    }
}
