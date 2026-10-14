using Full.NET.Modules.Ai.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只有 Worker 启动删除恢复循环；满批和失败也必须退让，避免外部故障导致忙循环。</summary>
internal sealed class AiKnowledgeCleanupHostedProcessor(IServiceScopeFactory scopes, IOptions<AiKnowledgeCleanupWorkerOptions> options,
    ILogger<AiKnowledgeCleanupHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                _ = await scope.ServiceProvider.GetRequiredService<AiKnowledgeCleanupWorkerCoordinator>()
                    .ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeCleanupIteration, exception); }
            try { await Task.Delay(configuration.PollMilliseconds, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
