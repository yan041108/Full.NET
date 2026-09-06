using Full.NET.Modules.ImportExport.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.ImportTasks;

/// <summary>轮询并处理 queued 导入任务批量执行。</summary>
internal sealed class ImportExportTaskHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ImportExportOptions> options,
    ILogger<ImportExportTaskHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ImportExportTaskHostedProcessorLog.IterationFailed(logger, exception);
            }

            var delay = TimeSpan.FromSeconds(Math.Max(options.CurrentValue.PollSeconds, 5));
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>执行一次 worker 迭代；集成测试可直接调用。</summary>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.ExecutionEnabled)
        {
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var processed = await scope.ServiceProvider
            .GetRequiredService<ImportExportTaskRunner>()
            .ProcessPendingAsync(cancellationToken)
            .ConfigureAwait(false);
        if (processed > 0)
        {
            ImportExportTaskHostedProcessorLog.IterationCompleted(logger, processed);
        }

        return processed;
    }
}

internal static partial class ImportExportTaskHostedProcessorLog
{
    [LoggerMessage(
        EventId = 6611,
        Level = LogLevel.Information,
        Message = "ImportExport task worker processed {ProcessedCount} tasks")]
    public static partial void IterationCompleted(ILogger logger, int processedCount);

    [LoggerMessage(
        EventId = 6612,
        Level = LogLevel.Error,
        Message = "ImportExport task worker iteration failed")]
    public static partial void IterationFailed(ILogger logger, Exception exception);
}
