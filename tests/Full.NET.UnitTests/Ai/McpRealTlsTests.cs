using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.Ai;

/// <summary>正式网络与 TLS 校验下的受控 MCP 远端连接。</summary>
[TestClass]
public sealed class McpRealTlsTests
{
    [TestMethod]
    public async Task Trusted_tls_succeeds_and_untrusted_certificate_is_rejected()
    {
        var trustedPath = Environment.GetEnvironmentVariable("FULLNET_A04_MCP_TRUSTED_PFX") ?? throw new InvalidOperationException("缺少测试证书。");
        var untrustedPath = Environment.GetEnvironmentVariable("FULLNET_A04_MCP_UNTRUSTED_PFX") ?? throw new InvalidOperationException("缺少测试证书。");
        using var trusted = X509CertificateLoader.LoadPkcs12FromFile(trustedPath, "", X509KeyStorageFlags.EphemeralKeySet);
        using var untrusted = X509CertificateLoader.LoadPkcs12FromFile(untrustedPath, "", X509KeyStorageFlags.EphemeralKeySet);
        static int ReservePort() { using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); return ((IPEndPoint)socket.LocalEndpoint).Port; }
        var trustedPort = ReservePort(); var untrustedPort = ReservePort(); Assert.AreNotEqual(trustedPort, untrustedPort);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, trustedPort, endpoint => endpoint.UseHttps(trusted));
            options.Listen(IPAddress.Loopback, untrustedPort, endpoint => endpoint.UseHttps(untrusted));
        });
        await using var app = builder.Build();
        var trustedRequests = 0; var untrustedRequests = 0; string? revision = null;
        app.MapPost("/mcp", async (HttpContext context) =>
        {
            if (context.Connection.LocalPort == trustedPort) Interlocked.Increment(ref trustedRequests);
            else Interlocked.Increment(ref untrustedRequests);
            using var request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (!request.RootElement.TryGetProperty("id", out var id)) { context.Response.StatusCode = 202; return; }
            var method = request.RootElement.GetProperty("method").GetString();
            object response;
            if (method == "initialize")
            {
                revision = request.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString();
                response = new { jsonrpc = "2.0", id = id.Clone(), result = new { protocolVersion = revision,
                    capabilities = new { tools = new { } }, serverInfo = new { name = "tls-fixture", version = "1" } } };
            }
            else if (method == "tools/list") response = new { jsonrpc = "2.0", id = id.Clone(), result = new { tools = Array.Empty<object>() } };
            else response = new { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } };
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, response, cancellationToken: context.RequestAborted);
        });
        await app.StartAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            var good = $"https://localhost:{trustedPort}"; var bad = $"https://localhost:{untrustedPort}";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Network:PrivateOnly"] = "true",
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = good,
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:1"] = bad,
            }).Build();
            var services = new ServiceCollection().AddLogging(); services.AddFullNetMcpClient(configuration);
            await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
            var client = await manager.GetClientAsync(Guid.CreateVersion7(), new Uri(good + "/mcp"), "synthetic-service-token", deadline.Token);
            Assert.AreEqual(0, (await client.ListToolsAsync(cancellationToken: deadline.Token)).Count);
            var error = await Assert.ThrowsAsync<Exception>(async () =>
                await manager.GetClientAsync(Guid.CreateVersion7(), new Uri(bad + "/mcp"), "synthetic-service-token", deadline.Token));
            Assert.IsTrue(error.ToString().Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || error.ToString().Contains("AuthenticationException", StringComparison.Ordinal), "拒绝必须来自证书校验。");
            Assert.IsFalse(deadline.IsCancellationRequested);
            Assert.IsTrue(Volatile.Read(ref trustedRequests) >= 2); Assert.AreEqual(0, Volatile.Read(ref untrustedRequests));
            Assert.IsFalse(string.IsNullOrWhiteSpace(revision));
            Console.WriteLine($"MCP real TLS: protocol={revision}, trustedRequests={trustedRequests}, untrustedHttpRequests={untrustedRequests}");
        }
        finally { await app.StopAsync(); }
    }
}
