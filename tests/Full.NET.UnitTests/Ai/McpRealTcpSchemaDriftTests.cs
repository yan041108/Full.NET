using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Full.NET.Agents.Mcp;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实 TCP 远端目录消失或漂移时，历史批准不能继续派发工具。</summary>
[TestClass]
public sealed class McpRealTcpSchemaDriftTests
{
    [TestMethod]
    [DataRow("unchanged")][DataRow("changed")][DataRow("removed")]
    public async Task Live_catalog_is_verified_before_remote_dispatch(string scenario)
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var origin = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var calls = 0;
        var lists = 0;
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
                    object response;
                    if (method == "initialize")
                    {
                        version = request.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString();
                        response = new { jsonrpc = "2.0", id = id.Clone(), result = new { protocolVersion = version,
                            capabilities = new { tools = new { } }, serverInfo = new { name = "schema-fixture", version = "1" } } };
                    }
                    else if (method == "tools/list")
                    {
                        Interlocked.Increment(ref lists);
                        var schema = scenario == "changed" ? "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}}}" : "{\"type\":\"object\",\"additionalProperties\":false}";
                        using var parsed = JsonDocument.Parse(schema);
                        object[] tools = scenario == "removed" ? [] : [new { name = "sample", inputSchema = parsed.RootElement.Clone() }];
                        response = new { jsonrpc = "2.0", id = id.Clone(), result = new { tools } };
                    }
                    else if (method == "tools/call")
                    {
                        Interlocked.Increment(ref calls);
                        response = new { jsonrpc = "2.0", id = id.Clone(), result = new { content = new[] { new { type = "text", text = "{\"ok\":true}" } }, isError = false } };
                    }
                    else response = new { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } };
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, deadline.Token);
                }
                finally { context.Response.Close(); }
            }
        });
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Network:PrivateOnly"] = "true", ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = origin,
            }).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddFullNetMcpClient(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
            const string approvedSchema = "{\"type\":\"object\",\"additionalProperties\":false}";
            var tool = new McpRemoteApprovedTool(Guid.CreateVersion7(), "fixture", new Uri(origin + "/mcp"),
                "remote.fixture.sample", "sample", 1, approvedSchema, McpRemoteCapabilityPolicy.ComputeSchemaHash(approvedSchema),
                "none", "ai.mcp.remote.invoke", McpRemoteCapabilityPolicy.ApprovedStatusKey, "synthetic-service-token");
            var handler = new McpRemoteToolHandler(tool, manager);
            using var json = JsonDocument.Parse("{}");
            var invocation = new ToolInvocation(Guid.CreateVersion7(), null, tool.LocalToolName, 1, json.RootElement);
            var actor = new ToolActor(Guid.CreateVersion7(), null, Guid.CreateVersion7());
            if (scenario == "unchanged")
                Assert.IsTrue((await handler.ExecuteAsync(invocation, actor, deadline.Token)).GetProperty("ok").GetBoolean());
            else
            {
                var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.ExecuteAsync(invocation, actor, deadline.Token).AsTask());
                Assert.AreEqual("ai.mcp.remote_schema_drift", error.Message);
            }
            Assert.AreEqual(1, Volatile.Read(ref lists));
            Assert.AreEqual(scenario == "unchanged" ? 1 : 0, Volatile.Read(ref calls));
            Assert.IsFalse(deadline.IsCancellationRequested);
            Console.WriteLine($"MCP real TCP schema={scenario}, protocol={version}, toolCalls={calls}");
        }
        finally { deadline.Cancel(); listener.Stop(); await server; }
    }
}
