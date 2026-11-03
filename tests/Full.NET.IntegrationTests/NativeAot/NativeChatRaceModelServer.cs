using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>隔离网络中的受控流，允许测试在真实模型响应期间从另一进程取消。</summary>
internal sealed class NativeChatRaceModelServer(WebApplication application) : IAsyncDisposable
{
    private TaskCompletionSource _entered = NewGate();
    private TaskCompletionSource _release = NewGate();
    private int _calls;
    internal int Calls => Volatile.Read(ref _calls);
    internal Task Entered => _entered.Task;
    internal void Resume() => _release.TrySetResult();
    internal void Next() { _entered = NewGate(); _release = NewGate(); }
    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task<NativeChatRaceModelServer> StartAsync()
    {
        var address = Environment.GetEnvironmentVariable("FULLNET_NATIVE_MODEL_ADDRESS");
        var certificate = Environment.GetEnvironmentVariable("FULLNET_NATIVE_MODEL_CERTIFICATE");
        Assert.IsTrue(IPAddress.TryParse(address, out var ip) && !IPAddress.IsLoopback(ip!), "必须提供受控非回环网络地址。");
        Assert.IsTrue(File.Exists(certificate), "缺少受控服务证书。");
        var resolved = await Dns.GetHostAddressesAsync("api.openai.com");
        Assert.IsTrue(resolved.Length == 1 && resolved[0].Equals(ip), "受控 DNS 必须精确指向本容器，禁止连接真实云。");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(ip!, 443,
            listener => listener.UseHttps(X509CertificateLoader.LoadPkcs12FromFile(certificate!, "fullnet-test-only"))));
        var app = builder.Build();
        var server = new NativeChatRaceModelServer(app);
        app.MapPost("/v1/chat/completions", async context =>
        {
            Assert.AreEqual("Bearer native-test-placeholder", context.Request.Headers.Authorization.ToString());
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            Assert.IsTrue(body.RootElement.GetProperty("stream").GetBoolean());
            Interlocked.Increment(ref server._calls);
            context.Response.ContentType = "text/event-stream";
            var frame = JsonSerializer.Serialize(new { id = "a06-controlled", @object = "chat.completion.chunk", created = 0,
                model = "gpt-4.1-mini", choices = new[] { new { index = 0, delta = new { role = "assistant", content = "可取消的受控流" }, finish_reason = (string?)null } } });
            await context.Response.WriteAsync("data: " + frame + "\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            server._entered.TrySetResult();
            try
            {
                await server._release.Task.WaitAsync(context.RequestAborted);
                await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        await app.StartAsync();
        return server;
    }

    public async ValueTask DisposeAsync() { _release.TrySetResult(); await application.StopAsync(); await application.DisposeAsync(); }
}
