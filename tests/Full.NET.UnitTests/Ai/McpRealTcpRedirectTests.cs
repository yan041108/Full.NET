using System.Net;
using System.Net.Sockets;
using Full.NET.AgenticWeb.Mcp.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>远端重定向不能把服务凭据转发到未经批准的目标。</summary>
[TestClass]
public sealed class McpRealTcpRedirectTests
{
    [TestMethod]
    public async Task Redirect_to_unapproved_origin_fails_without_contacting_target()
    {
        static int ReservePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
        var source = $"http://127.0.0.1:{ReservePort()}";
        var target = $"http://127.0.0.1:{ReservePort()}";
        Assert.AreNotEqual(source, target);
        using var sourceListener = new HttpListener();
        using var targetListener = new HttpListener();
        sourceListener.Prefixes.Add(source + "/");
        targetListener.Prefixes.Add(target + "/");
        sourceListener.Start(); targetListener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var sourceRequests = 0; var targetRequests = 0;
        var sourceTask = Task.Run(async () =>
        {
            while (!deadline.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await sourceListener.GetContextAsync().WaitAsync(deadline.Token); }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) when (deadline.IsCancellationRequested) { break; }
                Interlocked.Increment(ref sourceRequests);
                context.Response.StatusCode = (int)HttpStatusCode.TemporaryRedirect;
                context.Response.RedirectLocation = target + "/capture";
                context.Response.Close();
            }
        });
        var targetTask = Task.Run(async () =>
        {
            try
            {
                var context = await targetListener.GetContextAsync().WaitAsync(deadline.Token);
                Interlocked.Increment(ref targetRequests);
                context.Response.StatusCode = 500; context.Response.Close();
            }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) when (deadline.IsCancellationRequested) { }
        });
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Ai:Network:PrivateOnly"] = "true",
                ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = source,
            }).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddFullNetMcpClient(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<McpClientConnectionManager>();
            await Assert.ThrowsAsync<Exception>(async () =>
                await manager.GetClientAsync(Guid.CreateVersion7(), new Uri(source + "/mcp"), "synthetic-service-token", deadline.Token));
            Assert.IsFalse(deadline.IsCancellationRequested, "外层测试截止不能冒充拒绝。");
            Assert.IsTrue(Volatile.Read(ref sourceRequests) > 0);
            await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            Assert.AreEqual(0, Volatile.Read(ref targetRequests), "不得连接重定向目标或转发服务凭据。");
        }
        finally
        {
            deadline.Cancel(); sourceListener.Stop(); targetListener.Stop();
            await Task.WhenAll(sourceTask, targetTask);
        }
    }
}
