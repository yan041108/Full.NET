using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>仅 Worker 注册的无费用补偿循环；默认关闭，每轮失败或满批后仍退让并响应停机取消。</summary>
internal sealed class AiKnowledgeIncrementalRecoveryHostedProcessor(IServiceScopeFactory scopes,
    IOptions<AiKnowledgeIncrementalRecoveryWorkerOptions> options, ILogger<AiKnowledgeIncrementalRecoveryHostedProcessor> logger,
    AiDataProcessingPolicy? dataPolicy = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var observation = AiDataObservationScope.Enter(dataPolicy ?? AiDataProcessingPolicy.Default);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                _ = await scope.ServiceProvider.GetRequiredService<AiKnowledgeIncrementalRecoveryWorker>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception failure) { AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIncrementalRecoveryIteration, failure); }
            try { await Task.Delay(configuration.PollMilliseconds, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
