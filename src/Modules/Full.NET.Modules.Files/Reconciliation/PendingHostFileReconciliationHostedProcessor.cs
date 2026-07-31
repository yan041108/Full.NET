using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

internal sealed class PendingHostFileReconciliationHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PendingHostFileReconciliationOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<PendingHostFileReconciliationHostedProcessor> logger)
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
                PendingHostFileReconciliationLog.IterationFailed(
                    logger,
                    exception,
                    _provider);
            }

            await Task.Delay(
                    TimeSpan.FromSeconds(options.CurrentValue.PollSeconds),
                    stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<PendingHostFileReconciliationResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return PendingHostFileReconciliationResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<PendingHostFileReconciliationRunner>()
                .RunOnceAsync(currentOptions, cancellationToken)
                .ConfigureAwait(false);
            PendingHostFileReconciliationLog.IterationCompleted(
                logger,
                result.Promoted,
                result.Purged,
                result.ProbeFailures,
                result.RetainedPublishing,
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

internal static partial class PendingHostFileReconciliationLog
{
    [LoggerMessage(
        EventId = 4511,
        Level = LogLevel.Information,
        Message = "Files upload reconciliation promoted {Promoted}, purged {Purged}, retained {RetainedPublishing} publishing records, observed {ProbeFailures} probe failures and {ConcurrentCompletions} concurrent completions in {Batches} batches for {Provider}")]
    public static partial void IterationCompleted(
        ILogger logger,
        int promoted,
        int purged,
        int probeFailures,
        int retainedPublishing,
        int concurrentCompletions,
        int batches,
        DatabaseProvider provider);

    [LoggerMessage(
        EventId = 4512,
        Level = LogLevel.Error,
        Message = "Files upload reconciliation iteration failed for {Provider}")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception,
        DatabaseProvider provider);
}
