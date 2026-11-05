using Full.NET.Modules.ImportExport.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.ImportTasks;

/// <summary>清理独立于业务执行开关，按固定间隔有界推进，不因毒项或满批忙循环。</summary>
internal sealed class ImportSourceRetentionHostedProcessor(IServiceScopeFactory scopes,
    IOptionsMonitor<ImportExportOptions> options, ILogger<ImportSourceRetentionHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { ImportSourceRetentionLog.IterationFailed(logger, exception); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.SourceCleanupPollSeconds, 5, 3600)), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>提供同一 Worker 迭代入口，测试不需要等待真实轮询时间。</summary>
    internal async Task<int> ProcessOnceAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ImportSourceRetentionProcessor>().ProcessDueAsync(token).ConfigureAwait(false);
    }
}
