using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

/// <summary>API 节点定期从权威配置收敛诊断策略，请求热路径只读取内存快照。</summary>
internal sealed class DiagnosticPolicyRefreshService(DiagnosticPolicyStore store) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunAsync(RefreshInterval, stoppingToken);

    internal async Task RunAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await store.PollAuthorityAsync(stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await store.PollAuthorityAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host 停止时正常结束轮询。
        }
    }
}
