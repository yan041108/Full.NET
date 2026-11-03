using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>用真实回环 TCP 和正式网络策略验证远端协议故障不产生可用工具目录。</summary>
[TestClass]
public sealed class McpRealTcpFaultTests
{
    [TestMethod]
    [DataRow("malformed_json")]
    [DataRow("oversized_json")]
    [DataRow("oversized_sse")]
    [DataRow("disconnect")]
    public async Task Faulty_remote_catalog_is_rejected_without_a_tool_call(string fault)
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var origin = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var catalogRequests = 0;
        var calls = 0;
        string? version = null;
        var server = Task.Run(async () =>
        {
            while (!deadline.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(deadline.Token); }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) when (deadline.IsCancellationRequested) { break; }
                try
                {
                    if (context.Request.HttpMethod != "POST") { context.Response.StatusCode = 405; continue; }
                    using var request = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: deadline.Token);
                    if (!request.RootElement.TryGetProperty("id", out var id)) { context.Response.StatusCode = 202; continue; }
                    var method = request.RootElement.GetProperty("method").GetString();
                    if (method == "initialize")
                    {
                        version = request.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString();
                        await SendJson(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.Clone(), result = new
                            { protocolVersion = version, capabilities = new { tools = new { } }, serverInfo = new { name = "fault-fixture", version = "1" } } }));
                    }
                    else if (method == "tools/list")
                    {
                        Interlocked.Increment(ref catalogRequests);
                        if (fault == "disconnect") { context.Response.Abort(); continue; }
                        if (fault == "malformed_json") { await SendJson("{\"jsonrpc\":"); continue; }
                        var payload = new string('x', 4 * 1024 * 1024 + 8192);
                        if (fault == "oversized_json") { await SendJson("{\"padding\":\"" + payload + "\"}"); continue; }
                        context.Response.ContentType = "text/event-stream";
                        context.Response.SendChunked = true;
                        var bytes = Encoding.UTF8.GetBytes("event: message\ndata: {\"padding\":\"" + payload + "\"}\n\n");
                        await context.Response.OutputStream.WriteAsync(bytes, deadline.Token);
                    }
                    else
                    {
                        if (method == "tools/call") Interlocked.Increment(ref calls);
                        await SendJson(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } }));
                    }
                    async Task SendJson(string content)
                    {
                        var bytes = Encoding.UTF8.GetBytes(content);
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, deadline.Token);
                    }
                }
                catch (IOException) { /* 接收端超限后可提前关闭真实连接。 */ }
                catch (HttpListenerException) { /* 同上，按实际操作系统返回的断流保留。 */ }
                finally { context.Response.Close(); }
            }
        });
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Network:PrivateOnly"] = "true",
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = origin,
            }).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddFullNetMcpClient(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
            var client = await manager.GetClientAsync(Guid.CreateVersion7(), new Uri(origin + "/mcp"), "synthetic-service-token", deadline.Token);
            var error = await Assert.ThrowsAsync<Exception>(async () => await client.ListToolsAsync(cancellationToken: deadline.Token));
            Assert.IsFalse(deadline.IsCancellationRequested, "不能将测试超时当作受控拒绝。");
            Assert.AreEqual(1, Volatile.Read(ref catalogRequests));
            Assert.AreEqual(0, Volatile.Read(ref calls));
            Assert.IsFalse(string.IsNullOrWhiteSpace(version));
            Console.WriteLine($"MCP real TCP fault={fault}, protocol={version}, exception={error.GetType().Name}, toolCalls=0");
        }
        finally
        {
            deadline.Cancel();
            listener.Stop();
            await server;
        }
    }
}
