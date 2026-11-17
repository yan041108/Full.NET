using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Document.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.PreviewTasks;

/// <summary>轮询并处理 Host 文档 Office 预览转换任务。</summary>
internal sealed class DocumentPreviewTaskHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options,
    ILogger<DocumentPreviewTaskHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            TimeSpan? capacityDelay = null;
            try
            {
                processed = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServiceCapacityExceededException exception)
            {
                // 领取和执行遇到准入压力时一并暂停，恢复后按原 PDF 收据和租约继续。
                var pollingDelay = GetDelayAfterBatch(0);
                var minimumDelay = exception.RetryAfter > pollingDelay ? exception.RetryAfter : pollingDelay;
                capacityDelay = minimumDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(1000));
                DocumentPreviewTaskHostedProcessorLog.CapacityBackoff(logger, exception.Kind, capacityDelay.Value.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                DocumentPreviewTaskHostedProcessorLog.IterationFailed(logger, exception);
            }

            var delay = capacityDelay ?? GetDelayAfterBatch(processed);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var processed = await scope.ServiceProvider
                .GetRequiredService<DocumentPreviewTaskRunner>()
                .ProcessPendingAsync(cancellationToken)
                .ConfigureAwait(false);
            if (processed > 0)
            {
                DocumentPreviewTaskHostedProcessorLog.IterationCompleted(logger, processed);
            }

            return processed;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
    /// <summary>满批立即继续排空；空批、未满与故障后的零计数按当前配置退避。</summary>
    internal TimeSpan GetDelayAfterBatch(int processedCount) =>
        processedCount >= Math.Clamp(options.CurrentValue.BatchSize, 1, 50)
            ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(options.CurrentValue.PollSeconds, 5));

}

internal static partial class DocumentPreviewTaskHostedProcessorLog
{
    [LoggerMessage(
        EventId = 4711,
        Level = LogLevel.Information,
        Message = "Document preview task worker processed {ProcessedCount} tasks")]
    public static partial void IterationCompleted(ILogger logger, int processedCount);

    [LoggerMessage(
        EventId = 4712,
        Level = LogLevel.Error,
        Message = "Document preview task worker iteration failed")]
    public static partial void IterationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4713,
        Level = LogLevel.Warning,
        Message = "Document preview worker polling delayed by database capacity ({CapacityFailure}) for {RetryDelayMilliseconds} ms")]
    public static partial void CapacityBackoff(ILogger logger, ServiceCapacityFailureKind capacityFailure, double retryDelayMilliseconds);
}
