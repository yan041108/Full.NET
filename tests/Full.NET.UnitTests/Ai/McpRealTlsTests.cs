using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    [DataRow("untrusted")]
    [DataRow("wrong-name")]
    [DataRow("expired")]
    public async Task Trusted_tls_succeeds_and_untrusted_certificate_is_rejected(string rejectedCertificate)
    {
        using var root = CreateAuthority();
        using var otherRoot = CreateAuthority();
        using var trusted = CreateServerCertificate(root, "localhost", expired: false);
        using var untrusted = CreateServerCertificate(rejectedCertificate == "untrusted" ? otherRoot : root,
            rejectedCertificate == "wrong-name" ? "wrong.example" : "localhost", rejectedCertificate == "expired");
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
            services.AddHttpClient(nameof(McpClientConnectionManager)).ConfigurePrimaryHttpMessageHandler((handler, _) =>
            {
                // 只为本轮客户端增加独立信任根，复用正式网络策略处理器和系统 TLS 主机名/有效期校验。
                while (handler is DelegatingHandler layer) handler = layer.InnerHandler!;
                Assert.IsInstanceOfType<SocketsHttpHandler>(handler);
                var socketHandler = (SocketsHttpHandler)handler;
                Assert.IsNull(socketHandler.SslOptions.RemoteCertificateValidationCallback);
                var policy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                    VerificationFlags = X509VerificationFlags.NoFlag,
                    DisableCertificateDownloads = true,
                };
                policy.CustomTrustStore.Add(root);
                policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                socketHandler.SslOptions.CertificateChainPolicy = policy;
            });
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

    // 根和叶证书仅在内存中存活，不安装系统根、不读取用户密钥，也不依赖预置 PFX。
    private static X509Certificate2 CreateAuthority()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FullNET-MCP-" + Guid.NewGuid().ToString("N"), key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 issuer, string name, bool expired)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(name);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.Create(issuer, now.AddHours(-2), expired ? now.AddHours(-1) : now.AddHours(1),
            RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }
}
