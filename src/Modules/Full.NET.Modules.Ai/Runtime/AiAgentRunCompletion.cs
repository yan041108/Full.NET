using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Messaging;
using Full.NET.Agents.Runtime;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>运行收尾与预算在同一 AI 本地事务中收敛；模型与工具调用不得进入此边界。</summary>
internal sealed class AiAgentRunCompletion(
    ICommandTransaction transaction,
    AiAgentRunStore store,
    IAiOperationBudgetStore budget)
{
    /// <summary>只接受成功终态；失租不结算，结算失败连同状态、步骤、检查点和事件一起回滚。</summary>
    internal Task<bool> CommitAsync(AgentRunProgressCommit progress, AiOperationUsage usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.NewStatusKey != "completed")
            throw new InvalidOperationException("Only successful run completion can settle here.");
        return CommitCoreAsync(progress, usage, "succeeded", cancellationToken);
    }

    /// <summary>仅收敛允许结算的失败或会话拒绝；未知计量保留预留，需对账的派发不得调用此入口。</summary>
    internal Task<bool> RejectAsync(AgentRunProgressCommit progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var outcome = progress.NewStatusKey switch
        {
            "failed" => "failed",
            "authorization_required" => "cancelled",
            _ => throw new InvalidOperationException("Only failed or authorization-required runs can settle rejection.")
        };
        return CommitCoreAsync(progress, new(null, null), outcome, cancellationToken);
    }

    private Task<bool> CommitCoreAsync(AgentRunProgressCommit progress, AiOperationUsage usage, string outcome,
        CancellationToken cancellationToken)
    {
        AiExecutionBudget.ValidateUsage(usage);
        cancellationToken.ThrowIfCancellationRequested();
        return transaction.ExecuteAsync(async token =>
        {
            // 三个依赖由同一 DI 作用域解析，嵌套执行复用同一连接与事务。
            if (!await store.CommitProgressAsync(progress, token).ConfigureAwait(false)) return false;
            token.ThrowIfCancellationRequested();
            await budget.SettleAsync(progress.Lease.RunId, usage, outcome, token).ConfigureAwait(false);
            // 即使下层忽略取消并返回，也不能继续提交该事务。
            token.ThrowIfCancellationRequested();
            return true;
        }, cancellationToken);
    }
}
