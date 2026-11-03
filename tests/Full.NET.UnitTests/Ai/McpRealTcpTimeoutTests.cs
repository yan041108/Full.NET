using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实网络初始化无响应时，正式连接期限应结束等待并释放连接。</summary>
[TestClass]
public sealed class McpRealTcpTimeoutTests
{
    [TestMethod]
    public async Task Initialization_timeout_closes_connection_without_retrying()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var origin = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var requests = 0;
        var server = Task.Run(async () =>
        {
            HttpListenerContext? context = null;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(deadline.Token);
                Interlocked.Increment(ref requests);
                // 保持真实 TCP 连接而不发送响应，触发生产连接期限。
                await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (HttpListenerException) when (deadline.IsCancellationRequested) { }
            finally { context?.Response.Abort(); }
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
            var elapsed = Stopwatch.StartNew();
            await Assert.ThrowsAsync<Exception>(async () =>
                await manager.GetClientAsync(Guid.CreateVersion7(), new Uri(origin + "/mcp"), "synthetic-service-token", deadline.Token));
            elapsed.Stop();
            Assert.IsFalse(deadline.IsCancellationRequested, "外层测试期限不能冒充生产连接期限。");
            Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromSeconds(28) && elapsed.Elapsed < TimeSpan.FromSeconds(45));
            Assert.AreEqual(1, Volatile.Read(ref requests), "连接期限不得触发自动重试。");
            await scope.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(deadline.IsCancellationRequested, "作用域释放必须在服务端仍保持连接时完成。");
            Console.WriteLine($"MCP real TCP initialization timeout: {elapsed.Elapsed.TotalSeconds:F3}s, requests={requests}");
        }
        finally
        {
            deadline.Cancel();
            listener.Stop();
            await server;
        }
    }
}
