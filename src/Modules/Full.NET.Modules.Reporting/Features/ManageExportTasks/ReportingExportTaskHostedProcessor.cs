using Full.NET.Modules.Reporting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>轮询并恢复排队或租约到期的报表导出任务。</summary>
/// <param name="scopeFactory">Worker 作用域。</param>
/// <param name="options">导出配置。</param>
/// <param name="logger">日志。</param>
internal sealed class ReportingExportTaskHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ReportingExportOptions> options,
    ILogger<ReportingExportTaskHostedProcessor> logger) : BackgroundService
{
    /// <inheritdoc />
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
                ReportingExportTaskHostedProcessorLog.IterationFailed(logger, exception);
            }

            var delay = TimeSpan.FromSeconds(Math.Max(options.CurrentValue.PollSeconds, 5));
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>执行一次 worker 迭代；测试可直接调用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.ExecutionEnabled)
        {
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var processed = await scope.ServiceProvider
            .GetRequiredService<ReportingExportTaskRunner>()
            .ProcessPendingAsync(cancellationToken)
            .ConfigureAwait(false);
        if (processed > 0)
        {
            ReportingExportTaskHostedProcessorLog.IterationCompleted(logger, processed);
        }

        return processed;
    }
}

/// <summary>报表导出 Worker 日志。</summary>
internal static partial class ReportingExportTaskHostedProcessorLog
{
    /// <summary>记录本轮处理数量。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="processedCount">处理条数。</param>
    [LoggerMessage(
        EventId = 6711,
        Level = LogLevel.Information,
        Message = "Reporting export worker processed {ProcessedCount} tasks")]
    public static partial void IterationCompleted(ILogger logger, int processedCount);

    /// <summary>记录本轮未处理异常。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="exception">异常。</param>
    [LoggerMessage(
        EventId = 6712,
        Level = LogLevel.Error,
        Message = "Reporting export worker iteration failed")]
    public static partial void IterationFailed(ILogger logger, Exception exception);
}
