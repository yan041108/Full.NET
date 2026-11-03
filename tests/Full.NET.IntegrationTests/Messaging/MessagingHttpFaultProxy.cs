using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>仅存在于测试程序集的回环 HTTP 故障代理；保留真实请求/响应，可在执行前或响应返回前阻断。</summary>
internal sealed class MessagingHttpFaultProxy : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _upstream;
    private Gate? _gate;
    private readonly ConcurrentDictionary<Gate, byte> _activeGates = new();
    private int _resumeCount;
    internal Uri Address { get; }
    internal int ResumeCount => Volatile.Read(ref _resumeCount);
    internal bool Disconnect { get; set; }

    private MessagingHttpFaultProxy(WebApplication app, HttpClient upstream, Uri address)
    { _app = app; _upstream = upstream; Address = address; }

    internal static async Task<MessagingHttpFaultProxy> StartAsync(Uri upstream, bool dockerReachable = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls(dockerReachable ? "http://0.0.0.0:0" : "http://127.0.0.1:0");
        var app = builder.Build();
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = upstream, Timeout = TimeSpan.FromMinutes(8) };
        MessagingHttpFaultProxy? proxy = null;
        app.Run(context => proxy!.ForwardAsync(context));
        await app.StartAsync();
        var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        if (dockerReachable) address = new UriBuilder(address) { Host = "127.0.0.1" }.Uri;
        return proxy = new(app, client, address);
    }

    internal Gate Arm(string method, string path, bool afterExecution)
    {
        var gate = new Gate(method, path, afterExecution);
        if (Interlocked.CompareExchange(ref _gate, gate, null) is not null) throw new InvalidOperationException("已有测试故障未结束。");
        _activeGates.TryAdd(gate, 0);
        return gate;
    }

    private async Task ForwardAsync(HttpContext context)
    {
        if (Disconnect) { context.Abort(); return; }
        var path = context.Request.Path.Value!;
        if (context.Request.Method == "PUT" && path.EndsWith("/resume", StringComparison.Ordinal)) Interlocked.Increment(ref _resumeCount);
        var gate = _gate;
        if (gate is not null && gate.Method == context.Request.Method && gate.Path == path)
        {
            if (Interlocked.CompareExchange(ref _gate, null, gate) != gate) gate = null;
        }
        else gate = null;
        try
        {
            // 已被远端接收的控制命令不因原客户端断开而撤销；用于真实制造有限迟到动作。
            var executionToken = gate?.ContinueAfterClientDisconnect == true ? CancellationToken.None : context.RequestAborted;
            if (gate is { AfterExecution: false }) await gate.WaitAsync(executionToken);
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path + context.Request.QueryString);
            // API 的 Host 参与租户路由；代理只制造网络故障，不能悄悄改变可信域名输入。
            request.Headers.Host = context.Request.Host.Value;
            if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await _upstream.SendAsync(request, HttpCompletionOption.ResponseContentRead, executionToken);
            gate?.Executed.TrySetResult();
            if (gate is { AfterExecution: true })
            {
                gate.StatusCode = response.StatusCode;
                await gate.WaitAsync(context.RequestAborted);
            }
            if (gate?.DropResponse == true) { context.Abort(); return; }
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers)) context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (OperationCanceledException) { context.Abort(); }
        catch (HttpRequestException) { context.Abort(); }
        finally { if (gate is not null) _activeGates.TryRemove(gate, out _); }
    }

    public async ValueTask DisposeAsync()
    {
        _gate?.Release.TrySetResult();
        foreach (var gate in _activeGates.Keys) gate.Release.TrySetResult();
        await _app.StopAsync(); await _app.DisposeAsync(); _upstream.Dispose();
    }

    internal sealed class Gate(string method, string path, bool afterExecution)
    {
        internal string Method { get; } = method;
        internal string Path { get; } = path;
        internal bool AfterExecution { get; } = afterExecution;
        internal HttpStatusCode? StatusCode { get; set; }
        internal bool DropResponse { get; set; }
        internal bool ContinueAfterClientDisconnect { get; set; }
        internal TaskCompletionSource Executed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task WaitAsync(CancellationToken token) { Reached.TrySetResult(); await Release.Task.WaitAsync(token); }
    }
}
