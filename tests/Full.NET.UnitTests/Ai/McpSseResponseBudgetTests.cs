using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实 SDK/HTTP 验证 POST SSE 双向消息，以及没有可信长度头时的读取与释放预算。</summary>
[TestClass]
public sealed class McpSseResponseBudgetTests
{
    [TestMethod]
    public async Task Post_sse_allows_sdk_to_reply_to_reverse_ping_before_response_eof()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var origin = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        var pong = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServeAsync();
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Network:PrivateOnly"] = "true",
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = origin,
            }).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddFullNetMcpClient(configuration);
            using var provider = services.BuildServiceProvider();
            using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(McpClientConnectionManager));
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
                { Endpoint = new Uri(origin + "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http, ownsHttpClient: false);
            // 固定需要握手的协议版本，服务端可在原请求的 SSE 上发起反向 ping。
            await using var client = await McpClient.CreateAsync(transport,
                new ModelContextProtocol.Client.McpClientOptions { ProtocolVersion = "2025-11-25" }, cancellationToken: timeout.Token);
            await client.PingAsync(cancellationToken: timeout.Token);
            Assert.IsTrue(pong.Task.IsCompletedSuccessfully, "原 RPC 结束前，SDK 必须已读取并回复反向请求。");
        }
        finally
        {
            await timeout.CancelAsync();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        async Task ServeAsync()
        {
            var handlers = new List<Task>();
            try
            {
                while (!timeout.IsCancellationRequested)
                {
                    var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                    handlers.Add(HandleAsync(context));
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            catch (HttpListenerException) when (timeout.IsCancellationRequested) { }
            finally { await Task.WhenAll(handlers); }
        }

        async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod != "POST") { context.Response.StatusCode = 405; return; }
                using var request = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: timeout.Token);
                var root = request.RootElement;
                if (!root.TryGetProperty("method", out var method))
                {
                    Assert.AreEqual("server-probe", root.GetProperty("id").GetString());
                    Assert.IsTrue(root.TryGetProperty("result", out _));
                    pong.TrySetResult();
                    context.Response.StatusCode = 202;
                    return;
                }
                if (method.GetString() == "initialize")
                {
                    context.Response.ContentType = "application/json";
                    await WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                        + ",\"result\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"serverInfo\":{\"name\":\"budget-test\",\"version\":\"1\"}}}");
                }
                else if (method.GetString() == "ping")
                {
                    context.Response.ContentType = "text/event-stream";
                    context.Response.SendChunked = true;
                    await WriteAsync("event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":\"server-probe\",\"method\":\"ping\"}\n\n");
                    await context.Response.OutputStream.FlushAsync(timeout.Token);
                    // 在同一 SSE 返回原响应前等待客户端另一次 POST；完整缓冲会在这里互相等待。
                    await pong.Task.WaitAsync(timeout.Token);
                    await WriteAsync("event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":"
                        + root.GetProperty("id").GetRawText() + ",\"result\":{}}\n\n");
                }
                else context.Response.StatusCode = 202;

                async Task WriteAsync(string text) => await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(text), timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            finally { context.Response.Close(); }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Sse_byte_budget_rejects_unknown_or_forged_lengths_and_stays_failed(bool forgedLength)
    {
        var source = new UnannouncedStream(new byte[9]);
        var inner = new StreamContent(source);
        if (forgedLength) inner.Headers.ContentLength = 1;
        using var content = new BoundedMcpSseContent(inner, 8, TimeSpan.FromSeconds(5), default);
        var stream = await content.ReadAsStreamAsync();
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => stream.ReadAsync(new byte[16]).AsTask());
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => stream.ReadAsync(new byte[16]).AsTask());
        await stream.DisposeAsync();
        content.Dispose();
        Assert.IsFalse(source.CanRead);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Sse_deadline_and_caller_cancellation_interrupt_stalled_reads(bool cancelCaller)
    {
        var source = new StalledStream();
        using var caller = new CancellationTokenSource();
        using var content = new BoundedMcpSseContent(new StreamContent(source), 8,
            cancelCaller ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(2), default);
        var stream = await content.ReadAsStreamAsync();
        var read = stream.ReadAsync(new byte[1], caller.Token).AsTask();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancelCaller) caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        await stream.DisposeAsync();
        Assert.IsFalse(source.CanRead);
    }

    private sealed class UnannouncedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class StalledStream : MemoryStream
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
