using Full.NET.AI.Abstractions.Budgets;
using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Budgets;

/// <summary>只冻结供应商明确报告的核心用量；无效字段不能退化为未报告或授予剩余额度。</summary>
internal static class AiModelUsagePolicy
{
    /// <summary>缓存包含于输入，推理包含于输出；Embedding 无输出时沿用原零输出语义。</summary>
    internal static AiOperationUsage Read(UsageDetails? details, bool embedding = false)
    {
        var input = details?.InputTokenCount;
        var output = details?.OutputTokenCount ?? (embedding ? 0L : (long?)null);
        var cached = details?.CachedInputTokenCount;
        var reasoning = details?.ReasoningTokenCount;
        var total = details?.TotalTokenCount;
        var result = new AiOperationUsage(input, output, cached);
        AiExecutionBudget.ValidateUsage(result);
        if (reasoning is < 0 || reasoning is not null && (output is null || reasoning > output)
            || total is < 0 or > 2 * AiExecutionBudget.MaximumTokens
            || total is not null && (input is not null && total < input || output is not null && total < output)
            || total is not null && input is not null && output is not null && total != input + output)
            throw new AiBudgetException("ai.budget.invalid_usage");
        return result;
    }
}
