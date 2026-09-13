namespace Full.NET.Modules.Ai.Budgets;

/// <summary>服务端预算默认值；租户现有显式配额仍作为额外约束，HTTP 输入不能覆盖。</summary>
public sealed class AiOperationBudgetOptions
{
    public long MonthlyRequestLimit { get; set; } = 1000;
    public long MonthlyTokenLimit { get; set; } = 10_000_000;
    public long RunRequestLimit { get; set; } = 100;
    public long RunTokenLimit { get; set; } = 1_000_000;
    // 当前 Provider 没有硬费用计量认证；配置硬上限时明确拒绝，不把未知价格视作零。
    public decimal? MonthlyCostLimit { get; set; }
    public decimal? RunCostLimit { get; set; }
    public string Currency { get; set; } = "USD";
}
