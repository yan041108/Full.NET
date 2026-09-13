using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Worker 轮询 queued 运行；满批立即继续，避免 Poll 人为拉长积压。</summary>
internal sealed class AiAgentRunHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<AiAgentRuntimeOptions> options,
    ILogger<AiAgentRunHostedProcessor> logger) : BackgroundService
{
    private readonly AiAgentRuntimeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<AiAgentRunCoordinator>()
                    .ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI agent run worker iteration failed.");
            }

            if (processed < _options.BatchSize)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.PollMilliseconds), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
