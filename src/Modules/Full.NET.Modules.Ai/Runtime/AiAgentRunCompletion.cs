using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Messaging;
using Full.NET.Agents.Runtime;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>成功终态与预算在同一 AI 本地事务中收敛；模型与工具调用不得进入此边界。</summary>
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
        AiExecutionBudget.ValidateUsage(usage);
        cancellationToken.ThrowIfCancellationRequested();
        return transaction.ExecuteAsync(async token =>
        {
            // 三个依赖由同一 DI 作用域解析，嵌套执行复用同一连接与事务。
            if (!await store.CommitProgressAsync(progress, token).ConfigureAwait(false)) return false;
            token.ThrowIfCancellationRequested();
            await budget.SettleAsync(progress.Lease.RunId, usage, "succeeded", token).ConfigureAwait(false);
            // 即使下层忽略取消并返回，也不能继续提交该事务。
            token.ThrowIfCancellationRequested();
            return true;
        }, cancellationToken);
    }
}
