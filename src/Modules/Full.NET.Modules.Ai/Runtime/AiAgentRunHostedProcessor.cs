using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Worker 领取运行并修复历史终态预算；满批立即继续，避免 Poll 人为拉长积压。</summary>
internal sealed class AiAgentRunHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<AiAgentRuntimeOptions> options,
    ILogger<AiAgentRunHostedProcessor> logger, AiDataProcessingPolicy? dataPolicy = null) : BackgroundService
{
    private readonly AiAgentRuntimeOptions _options = options.Value;
    // 游标属于宿主而非每轮 Scoped 处理器，坏记录不阻断后续，扫到末尾后重试此前失败项。
    private Guid? _terminalBudgetCursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingDelay = TimeSpan.FromMilliseconds(_options.PollMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var observationScope = AiDataObservationScope.Enter(dataPolicy ?? AiDataProcessingPolicy.Default);
            var processed = 0;
            TimeSpan? capacityDelay = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<AiAgentRunCoordinator>()
                    .ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
                var repair = scope.ServiceProvider.GetRequiredService<AiAgentTerminalBudgetRepairProcessor>();
                try
                {
                    processed += await repair.ProcessPendingAsync(stoppingToken, _terminalBudgetCursor).ConfigureAwait(false);
                }
                finally
                {
                    _terminalBudgetCursor = repair.LastScannedRunId;
                }
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServiceCapacityExceededException exception)
            {
                // 即使领取已满批，后续扫描遇到准入压力也要退避并错开实例重试。
                var minimum = exception.RetryAfter > pollingDelay ? exception.RetryAfter : pollingDelay;
                var jitterLimit = Math.Min(pollingDelay.TotalMilliseconds / 4, 1000);
                capacityDelay = minimum + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * jitterLimit);
                AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.AgentRunIteration, exception);
            }
            catch (Exception exception)
            {
                AiFailureLog.Write(logger, LogLevel.Error, AiFailureStage.AgentRunIteration, exception);
            }

            if (capacityDelay.HasValue || processed < _options.BatchSize)
            {
                await Task.Delay(capacityDelay ?? pollingDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
