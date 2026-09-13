using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class McpClientConnectionManagerTests
{
    private static readonly Uri Endpoint = new("https://unit.test/mcp");

    [TestMethod]
    public async Task Cancelling_first_waiter_does_not_cancel_shared_initialization()
    {
        var factory = new ProtocolFactory(blockInitialization: true);
        await using var manager = new McpClientConnectionManager(factory,
            Options.Create(new McpClientOptions { ApprovedOrigins = ["https://unit.test"] }));
        using var firstCancellation = new CancellationTokenSource();
        var id = Guid.CreateVersion7();
        var first = manager.GetClientAsync(id, Endpoint, "token", firstCancellation.Token).AsTask();
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = manager.GetClientAsync(id, Endpoint, "token", CancellationToken.None).AsTask();
        firstCancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        factory.Release.TrySetResult();
        Assert.IsNotNull(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, factory.Clients.Count);
    }

    [TestMethod]
    public async Task Concurrent_callers_share_one_client_and_scope_disposes_it()
    {
        var factory = new ProtocolFactory();
        var manager = new McpClientConnectionManager(factory,
            Options.Create(new McpClientOptions { ApprovedOrigins = ["https://unit.test"] }));
        var id = Guid.CreateVersion7();
        var pending = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            await manager.GetClientAsync(id, Endpoint, "token", CancellationToken.None))).ToArray();
        var clients = await Task.WhenAll(pending);
        Assert.AreEqual(1, factory.Clients.Count);
        Assert.IsTrue(clients.All(client => ReferenceEquals(client, clients[0])));
        await Task.WhenAll(manager.DisposeAsync().AsTask(), manager.DisposeAsync().AsTask());
        Assert.IsTrue(factory.Clients.All(client => client.Disposed));
    }

    [TestMethod]
    public async Task Scope_disposal_cancels_pending_initialization_and_releases_http()
    {
        var factory = new ProtocolFactory(blockInitialization: true);
        var manager = new McpClientConnectionManager(factory,
            Options.Create(new McpClientOptions { ApprovedOrigins = ["https://unit.test"] }));
        var pending = manager.GetClientAsync(Guid.CreateVersion7(), Endpoint, "token", CancellationToken.None).AsTask();
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.IsTrue(factory.Clients.All(client => client.Disposed));
    }

    [TestMethod]
    public async Task Failed_connection_releases_http_client_and_can_retry()
    {
        var factory = new FailingFactory();
        await using var manager = new McpClientConnectionManager(factory,
            Options.Create(new McpClientOptions { ApprovedOrigins = ["https://unit.test"] }));
        var id = Guid.CreateVersion7();
        for (var i = 0; i < 2; i++)
        {
            try
            {
                await manager.GetClientAsync(id, Endpoint, "service-token", CancellationToken.None);
                Assert.Fail("连接失败必须向调用方传播。");
            }
            catch (Exception exception) when (exception is not AssertFailedException) { }
        }
        Assert.AreEqual(2, factory.Clients.Count);
        Assert.IsTrue(factory.Clients.All(client => client.Disposed));
    }

    [TestMethod]
    public async Task Disposed_manager_rejects_new_connections()
    {
        var factory = new FailingFactory();
        var manager = new McpClientConnectionManager(factory,
            Options.Create(new McpClientOptions { ApprovedOrigins = ["https://unit.test"] }));
        await manager.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.GetClientAsync(Guid.CreateVersion7(), Endpoint, "token", CancellationToken.None).AsTask());
        Assert.AreEqual(0, factory.Clients.Count);
    }

    private sealed class FailingFactory : IHttpClientFactory
    {
        public List<TrackingClient> Clients { get; } = [];
        public HttpClient CreateClient(string name)
        {
            var client = new TrackingClient();
            Clients.Add(client);
            return client;
        }
    }

    private sealed class TrackingClient(HttpMessageHandler? handler = null) : HttpClient(handler ?? new FailingHandler())
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Test transport failure"));
    }

    private sealed class ProtocolFactory(bool blockInitialization = false) : IHttpClientFactory
    {
        public System.Collections.Concurrent.ConcurrentBag<TrackingClient> Clients { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HttpClient CreateClient(string name)
        {
            var client = new TrackingClient(new ProtocolHandler(blockInitialization, Started, Release));
            Clients.Add(client);
            return client;
        }
    }

    private sealed class ProtocolHandler(bool block, TaskCompletionSource started, TaskCompletionSource release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post) return new(HttpStatusCode.MethodNotAllowed);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (!body.RootElement.TryGetProperty("id", out var id)) return new(HttpStatusCode.Accepted);
            started.TrySetResult();
            if (block) await release.Task.WaitAsync(cancellationToken);
            await Task.Yield();
            // 模拟仅支持 initialize 的远端；新版 SDK 会先探测 discover，再回退。
            if (body.RootElement.GetProperty("method").GetString() != "initialize")
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } }),
                        Encoding.UTF8, "application/json")
                };
            var version = body.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id = id.Clone(), result = new
                    { protocolVersion = version, capabilities = new { }, serverInfo = new { name = "test", version = "1" } }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
