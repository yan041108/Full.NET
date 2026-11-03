using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Files.Features.WorkflowReadCapabilities;

/// <summary>过期只收敛Files元数据；对象仍由原持有者管理，不借用目标业务事务。</summary>
internal sealed class WorkflowFileReadCapabilityCleanupHostedProcessor(IServiceScopeFactory scopes,
    ILogger<WorkflowFileReadCapabilityCleanupHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                await scope.ServiceProvider.GetRequiredService<WorkflowFileReadCapabilityStore>().ExpireAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Workflow file capability expiry awaits a later bounded scan."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
