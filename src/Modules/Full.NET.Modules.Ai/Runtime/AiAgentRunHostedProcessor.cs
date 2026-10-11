using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Worker 领取运行并修复历史终态预算；满批立即继续，避免 Poll 人为拉长积压。</summary>
internal sealed class AiAgentRunHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<AiAgentRuntimeOptions> options,
    ILogger<AiAgentRunHostedProcessor> logger) : BackgroundService
{
    private readonly AiAgentRuntimeOptions _options = options.Value;
    // 游标属于宿主而非每轮 Scoped 处理器，坏记录不阻断后续，扫到末尾后重试此前失败项。
    private Guid? _terminalBudgetCursor;

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
                var repair = scope.ServiceProvider.GetRequiredService<AiAgentTerminalBudgetRepairProcessor>();
                processed += await repair.ProcessPendingAsync(stoppingToken, _terminalBudgetCursor).ConfigureAwait(false);
                _terminalBudgetCursor = repair.LastScannedRunId;
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
