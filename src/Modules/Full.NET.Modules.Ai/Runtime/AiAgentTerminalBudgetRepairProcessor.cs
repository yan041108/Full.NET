using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Worker 有界扫描历史终态，每个运行独立作用域和事务，单项故障不阻塞批内其他修复。</summary>
internal sealed class AiAgentTerminalBudgetRepairProcessor(IServiceScopeFactory scopeFactory, AiAgentRunStore store,
    IOptions<AiAgentRuntimeOptions> options, ILogger<AiAgentTerminalBudgetRepairProcessor> logger)
{
    /// <summary>包括拒绝和失败记录的最后扫描位置；空批重置，由宿主跨作用域保存。</summary>
    internal Guid? LastScannedRunId { get; private set; }

    internal async Task<int> ProcessPendingAsync(CancellationToken cancellationToken, Guid? afterRunId = null)
    {
        var reconcileReceipts = options.Value.ReconcileCompletedReceipts;
        var runIds = await store.ScanTerminalUnsettledAsync(options.Value.BatchSize, cancellationToken, afterRunId, reconcileReceipts).ConfigureAwait(false);
        LastScannedRunId = null;
        var repaired = 0;
        foreach (var runId in runIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastScannedRunId = runId;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var run = await scope.ServiceProvider.GetRequiredService<AiAgentRunStore>().FindByIdAsync(runId, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (run is null || AiAgentTerminalBudgetRepair.Outcome(run.StatusKey) is null
                    && !(reconcileReceipts && run.StatusKey == "reconciliation_required")) continue;
                var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                // 只接纳持久行的精确 Host/租户绑定，不用请求参数或用户会话决定账本归属。
                if (run.TenantId is { } id && id != Guid.Empty && run.ScopeKey == id.ToString("N"))
                    writer.SetTenant(new(id, run.ScopeKey, run.ScopeKey));
                else if (run.TenantId is null && run.ScopeKey == "host") writer.SetHost();
                else continue;
                try
                {
                    var changed = run.StatusKey == "reconciliation_required"
                        ? await scope.ServiceProvider.GetRequiredService<AiAgentCompletedReceiptReconciliation>().ReconcileAsync(runId, cancellationToken).ConfigureAwait(false)
                        : await scope.ServiceProvider.GetRequiredService<AiAgentTerminalBudgetRepair>().RepairAsync(runId, cancellationToken).ConfigureAwait(false);
                    if (changed) repaired++;
                }
                finally { writer.Clear(); }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { AiFailureLog.Write(logger, LogLevel.Error, AiFailureStage.AgentBudgetRepair, exception, runId); }
        }
        return repaired;
    }
}
