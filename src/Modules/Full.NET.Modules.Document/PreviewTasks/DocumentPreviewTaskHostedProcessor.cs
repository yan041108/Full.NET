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
                DocumentPreviewTaskHostedProcessorLog.IterationFailed(logger, exception);
            }

            var delay = TimeSpan.FromSeconds(Math.Max(options.CurrentValue.PollSeconds, 5));
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            return 0;
        }

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
}
