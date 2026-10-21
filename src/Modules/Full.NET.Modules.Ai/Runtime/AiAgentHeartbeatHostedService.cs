using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>心跳独立于执行批次，长模型调用不会让仍存活的 Worker 被准入误判为离线。</summary>
internal sealed class AiAgentHeartbeatHostedService(
    AiAgentWorkerHeartbeatService heartbeat,
    IOptions<AiAgentRuntimeOptions> options,
    ILogger<AiAgentHeartbeatHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollMilliseconds));
        do
        {
            try
            {
                await heartbeat.UpsertAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI Agent Worker heartbeat failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
