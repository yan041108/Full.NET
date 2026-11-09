using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>显式开启的 Worker 轮询；API 不注册此循环。</summary>
internal sealed class AiKnowledgeIndexHostedProcessor(IServiceScopeFactory scopes, IOptions<AiKnowledgeIndexWorkerOptions> options,
    ILogger<AiKnowledgeIndexHostedProcessor> logger, AiDataProcessingPolicy? dataPolicy = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled && !configuration.CorrectionPreparationEnabled) return;
        var pollingDelay = TimeSpan.FromMilliseconds(configuration.PollMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var observationScope = AiDataObservationScope.Enter(dataPolicy ?? AiDataProcessingPolicy.Default);
            var acquired = 0;
            TimeSpan? capacityDelay = null;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (configuration.CorrectionPreparationEnabled)
                    acquired = await scope.ServiceProvider.GetRequiredService<AiKnowledgeCorrectionPreparationCoordinator>()
                        .ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
                if (configuration.Enabled)
                    acquired += await scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexWorkerCoordinator>()
                        .ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                if (exception is ServiceCapacityExceededException capacity)
                    capacityDelay = AiWorkerAdmissionBackoff.GetDelay(pollingDelay, capacity.RetryAfter);
                AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIndexIteration, exception);
            }
            // 满批包含已领取的失败任务，立即继续；竞争未领取的候选仍按间隔退让。
            if (capacityDelay.HasValue || acquired < configuration.BatchSize)
            {
                try { await Task.Delay(capacityDelay ?? pollingDelay, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
