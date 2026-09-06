using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Notifications.Configuration;

namespace Full.NET.Modules.Notifications.Features.ManageDingTalkApprovalSync;

/// <summary>Worker 轮询钉钉审批镜像同步；只维护出站与状态镜像，不驱动 Workflow 决策。</summary>
internal sealed class DingTalkApprovalSyncHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<DingTalkApprovalSyncOptions> options,
    ILogger<DingTalkApprovalSyncHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DingTalkApprovalSyncService>();
                await service.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "DingTalk approval sync worker iteration failed.");
            }

            var delay = TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 15, 3600));
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
