using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>通过真实回环 TCP 验证服务凭据轮换，出站仍使用正式网络策略。</summary>
[TestClass]
public sealed class McpRealTcpCredentialTests
{
    [TestMethod]
    public async Task Revocation_is_enforced_by_remote_and_new_scope_uses_rotated_credential()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var origin = $"http://127.0.0.1:{port}";
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var acceptedToken = "first-service-token";
        var rejectedRequests = 0;
        var servedCalls = 0;
        string? negotiatedVersion = null;
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(stop.Token); }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) when (stop.IsCancellationRequested) { break; }
                try
                {
                    if (context.Request.Headers["Authorization"] != "Bearer " + Volatile.Read(ref acceptedToken))
                    {
                        Interlocked.Increment(ref rejectedRequests);
                        context.Response.StatusCode = 401;
                        continue;
                    }
                    if (context.Request.HttpMethod != "POST")
                    {
                        context.Response.StatusCode = 405;
                        continue;
                    }
                    using var json = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: stop.Token);
                    if (!json.RootElement.TryGetProperty("id", out var id))
                    {
                        context.Response.StatusCode = 202;
                        continue;
                    }
                    var method = json.RootElement.GetProperty("method").GetString();
                    object reply;
                    if (method == "initialize")
                    {
                        var version = json.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString();
                        Volatile.Write(ref negotiatedVersion, version);
                        reply = new { jsonrpc = "2.0", id = id.Clone(), result = new { protocolVersion = version, capabilities = new { tools = new { } }, serverInfo = new { name = "tcp-fixture", version = "1" } } };
                    }
                    else if (method == "tools/list")
                    {
                        Interlocked.Increment(ref servedCalls);
                        reply = new { jsonrpc = "2.0", id = id.Clone(), result = new { tools = Array.Empty<object>() } };
                    }
                    else reply = new { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } };
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply));
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, stop.Token);
                }
                finally { context.Response.Close(); }
            }
        });
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = origin,
                ["FullNet:Ai:Network:PrivateOnly"] = "true",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFullNetMcpClient(config);
            await using var provider = services.BuildServiceProvider();
            var id = Guid.CreateVersion7();
            await using (var scope = provider.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
                var client = await manager.GetClientAsync(id, new Uri(origin + "/mcp"), "first-service-token", stop.Token);
                Assert.AreEqual(0, (await client.ListToolsAsync(cancellationToken: stop.Token)).Count);
                Volatile.Write(ref acceptedToken, "rotated-service-token");
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => manager.GetClientAsync(
                    id, new Uri(origin + "/mcp"), "rotated-service-token", stop.Token).AsTask());
                await Assert.ThrowsAsync<Exception>(async () => await client.ListToolsAsync(cancellationToken: stop.Token));
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
                var client = await manager.GetClientAsync(id, new Uri(origin + "/mcp"), "rotated-service-token", stop.Token);
                Assert.AreEqual(0, (await client.ListToolsAsync(cancellationToken: stop.Token)).Count);
            }
            Assert.IsTrue(Volatile.Read(ref rejectedRequests) > 0);
            Assert.AreEqual(2, Volatile.Read(ref servedCalls));
            Assert.IsFalse(string.IsNullOrWhiteSpace(Volatile.Read(ref negotiatedVersion)));
            Console.WriteLine($"MCP initialize protocol version: {Volatile.Read(ref negotiatedVersion)}");
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            await server;
        }
    }
}
