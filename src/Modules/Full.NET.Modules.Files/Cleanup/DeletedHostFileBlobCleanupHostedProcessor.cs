using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Cleanup;

internal sealed class DeletedHostFileBlobCleanupHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DeletedHostFileBlobCleanupOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<DeletedHostFileBlobCleanupHostedProcessor> logger)
    : BackgroundService
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

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
                DeletedHostFileBlobCleanupLog.IterationFailed(
                    logger,
                    exception,
                    _provider);
            }

            var delay = TimeSpan.FromSeconds(options.CurrentValue.PollSeconds);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<DeletedHostFileBlobCleanupResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return DeletedHostFileBlobCleanupResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<DeletedHostFileBlobCleanupRunner>()
                .RunOnceAsync(currentOptions, cancellationToken)
                .ConfigureAwait(false);
            DeletedHostFileBlobCleanupLog.IterationCompleted(
                logger,
                result.Purged,
                result.BlobFailures,
                result.ConcurrentlyCompleted,
                result.BatchesExecuted,
                _provider);
            return result;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}

internal static partial class DeletedHostFileBlobCleanupLog
{
    [LoggerMessage(
        EventId = 4501,
        Level = LogLevel.Information,
        Message = "Files cleanup purged {Purged} tombstones, observed {BlobFailures} blob failures and {ConcurrentlyCompleted} concurrent completions in {Batches} batches for {Provider}")]
    public static partial void IterationCompleted(
        ILogger logger,
        int purged,
        int blobFailures,
        int concurrentlyCompleted,
        int batches,
        DatabaseProvider provider);

    [LoggerMessage(
        EventId = 4502,
        Level = LogLevel.Error,
        Message = "Files cleanup iteration failed for {Provider}")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception,
        DatabaseProvider provider);
}
