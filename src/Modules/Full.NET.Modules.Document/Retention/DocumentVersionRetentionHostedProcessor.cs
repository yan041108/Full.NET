using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Document.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Retention;

internal sealed class DocumentVersionRetentionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DocumentVersionRetentionOptions> options,
    ILogger<DocumentVersionRetentionHostedProcessor> logger) : BackgroundService
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
                DocumentVersionRetentionHostedProcessorLog.IterationFailed(logger, exception);
            }

            var delay = TimeSpan.FromSeconds(Math.Max(options.CurrentValue.PollSeconds, 60));
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<DocumentVersionRetentionResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (currentOptions.MaximumRetainedHistoryVersions <= 0)
        {
            return DocumentVersionRetentionResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<DocumentVersionRetentionRunner>()
                .RunOnceAsync(currentOptions, cancellationToken)
                .ConfigureAwait(false);
            DocumentVersionRetentionHostedProcessorLog.IterationCompleted(
                logger,
                result.VersionsDeleted,
                result.ItemsProcessed);
            return result;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}

internal static partial class DocumentVersionRetentionHostedProcessorLog
{
    [LoggerMessage(
        EventId = 4701,
        Level = LogLevel.Information,
        Message = "Document version retention deleted {DeletedVersions} versions across {ItemsProcessed} items")]
    public static partial void IterationCompleted(
        ILogger logger,
        int deletedVersions,
        int itemsProcessed);

    [LoggerMessage(
        EventId = 4702,
        Level = LogLevel.Error,
        Message = "Document version retention iteration failed")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception);
}
