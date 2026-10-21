using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>计划派发和实例心跳独立于业务执行槽，长批次不能阻止其他实例领取新到期任务。</summary>
internal sealed class JobsControlPlaneHostedProcessor(
    IServiceScopeFactory scopes,
    JobWorkerHeartbeatService heartbeat,
    IOptions<JobsWorkerOptions> options,
    ILogger<JobsControlPlaneHostedProcessor> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        RunLoopAsync(ProcessSchedulesOnceAsync, "schedule", stoppingToken),
        RunLoopAsync(heartbeat.UpsertAsync, "heartbeat", stoppingToken));

    internal async Task ProcessSchedulesOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            await scope.ServiceProvider.GetRequiredService<JobScheduleDispatcher>()
                .ProcessDueAsync(options.Value.BatchSize, cancellationToken).ConfigureAwait(false);
        }
        finally { tenant.Clear(); }
    }

    private async Task RunLoopAsync(Func<CancellationToken, Task> process, string role, CancellationToken token)
    {
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollMilliseconds));
        do
        {
            try { await process(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Jobs {Role} iteration failed.", role); }
        }
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
    }
}
