using Full.NET.LogConsumer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>独立运维面与系统信号生命周期；不装配业务模块或迁移。</summary>
internal sealed class ConsumerOperationsHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly CancellationTokenRegistration _stoppingRegistration;

    private ConsumerOperationsHost(IHost host, ConsumerRuntimeState state)
    {
        _host = host;
        StoppingToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        _stoppingRegistration = StoppingToken.Register(state.Stop);
    }

    public CancellationToken StoppingToken { get; }

    public static async Task<ConsumerOperationsHost> StartAsync(int port, ConsumerRuntimeState state)
    {
        IHost host;
        if (port == 0)
        {
            // 兼容既有 CLI：关闭 HTTP 时仍通过 ConsoleLifetime 接收 SIGTERM，不监听随机端口。
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            host = builder.Build();
        }
        else
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            var application = builder.Build();
            // live 与 Kafka/ES 故障解耦；ready 依据 Poll 回调实际取得的分区，绝不表示 ES 已写入。
            application.MapGet("/health/live", () => Results.Text(
                state.IsStopping ? "stopping" : "live", statusCode: state.IsStopping ? 503 : 200));
            application.MapGet("/health/ready", () => Results.Text(
                state.IsReady ? "ready" : "not-ready", statusCode: state.IsReady ? 200 : 503));
            application.MapGet("/metrics", () => Results.Text(state.Metrics(),
                "text/plain; version=0.0.4; charset=utf-8"));
            host = application;
        }

        var operations = new ConsumerOperationsHost(host, state);
        try
        {
            await host.StartAsync();
            return operations;
        }
        catch
        {
            await operations.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _stoppingRegistration.Dispose();
            if (_host is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
            else _host.Dispose();
        }
    }
}
