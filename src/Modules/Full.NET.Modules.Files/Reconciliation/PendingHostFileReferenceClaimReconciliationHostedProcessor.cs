using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

internal sealed class PendingHostFileReferenceClaimReconciliationHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PendingHostFileReferenceClaimReconciliationOptions> options,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<PendingHostFileReferenceClaimReconciliationHostedProcessor> logger)
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
                logger.LogError(
                    exception,
                    "Files reference claim reconciliation iteration failed for {Provider}",
                    _provider);
            }

            await Task.Delay(
                    TimeSpan.FromSeconds(options.CurrentValue.PollSeconds),
                    stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<PendingHostFileReferenceClaimReconciliationResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return PendingHostFileReferenceClaimReconciliationResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            return await scope.ServiceProvider
                .GetRequiredService<PendingHostFileReferenceClaimReconciliationRunner>()
                .RunOnceAsync(currentOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}