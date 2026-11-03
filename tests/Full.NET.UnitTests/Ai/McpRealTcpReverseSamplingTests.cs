using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Full.NET.UnitTests.Ai;

/// <summary>远端反向采样请求不得获得未配置的本地模型能力。</summary>
[TestClass]
public sealed class McpRealTcpReverseSamplingTests
{
    [TestMethod]
    public async Task Reverse_sampling_is_rejected_before_catalog_completes()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start(); var origin = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new HttpListener(); listener.Prefixes.Add(origin + "/"); listener.Start();
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServeAsync();
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Network:PrivateOnly"] = "true",
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = origin,
            }).Build();
            var services = new ServiceCollection().AddLogging(); services.AddFullNetMcpClient(configuration);
            using var provider = services.BuildServiceProvider();
            using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(McpClientConnectionManager));
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
                { Endpoint = new Uri(origin + "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http, ownsHttpClient: false);
            await using var client = await McpClient.CreateAsync(transport,
                new ModelContextProtocol.Client.McpClientOptions { ProtocolVersion = "2025-11-25" }, cancellationToken: deadline.Token);
            var tools = await client.ListToolsAsync(cancellationToken: deadline.Token);
            Assert.AreEqual(0, tools.Count);
            Assert.IsTrue(rejected.Task.IsCompletedSuccessfully, "目录结束前 SDK 必须以协议错误拒绝反向采样。");
            Assert.IsFalse(deadline.IsCancellationRequested);
        }
        finally { await deadline.CancelAsync(); listener.Stop(); await server.WaitAsync(TimeSpan.FromSeconds(5)); }

        async Task ServeAsync()
        {
            var handlers = new List<Task>();
            try
            {
                while (!deadline.IsCancellationRequested)
                    handlers.Add(HandleAsync(await listener.GetContextAsync().WaitAsync(deadline.Token)));
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (HttpListenerException) when (deadline.IsCancellationRequested) { }
            finally { await Task.WhenAll(handlers); }
        }

        async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod != "POST") { context.Response.StatusCode = 405; return; }
                using var request = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: deadline.Token);
                var root = request.RootElement;
                if (!root.TryGetProperty("method", out var method))
                {
                    Assert.AreEqual("sampling-probe", root.GetProperty("id").GetString());
                    Assert.IsTrue(root.TryGetProperty("error", out _), "未配置采样时必须返回错误，不能返回模型结果。");
                    Assert.IsFalse(root.TryGetProperty("result", out _));
                    rejected.TrySetResult(); context.Response.StatusCode = 202; return;
                }
                if (method.GetString() == "initialize")
                {
                    context.Response.ContentType = "application/json";
                    await WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                        + ",\"result\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"reverse-test\",\"version\":\"1\"}}}");
                }
                else if (method.GetString() == "tools/list")
                {
                    context.Response.ContentType = "text/event-stream"; context.Response.SendChunked = true;
                    await WriteAsync("event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":\"sampling-probe\",\"method\":\"sampling/createMessage\",\"params\":{\"messages\":[],\"maxTokens\":1}}\n\n");
                    await context.Response.OutputStream.FlushAsync(deadline.Token);
                    await rejected.Task.WaitAsync(deadline.Token);
                    await WriteAsync("event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":"
                        + root.GetProperty("id").GetRawText() + ",\"result\":{\"tools\":[]}}\n\n");
                }
                else context.Response.StatusCode = 202;
                async Task WriteAsync(string value) => await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(value), deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            finally { context.Response.Close(); }
        }
    }
}
