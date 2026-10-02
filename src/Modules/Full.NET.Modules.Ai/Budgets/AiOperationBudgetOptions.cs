namespace Full.NET.Modules.Ai.Budgets;

/// <summary>服务端预算默认值；租户现有显式配额仍作为额外约束，HTTP 输入不能覆盖。</summary>
public sealed class AiOperationBudgetOptions
{
    /// <summary>月度请求数上限；默认 1000，超过后当月拒绝新的 AI 操作请求。</summary>
    public long MonthlyRequestLimit { get; set; } = 1000;

    /// <summary>月度 Token 消耗上限；默认 10000000，超过后当月拒绝新请求。</summary>
    public long MonthlyTokenLimit { get; set; } = 10_000_000;

    /// <summary>单次运行请求数上限；默认 100，超过后该次运行被拒绝。</summary>
    public long RunRequestLimit { get; set; } = 100;

    /// <summary>单次运行 Token 消耗上限；默认 1000000，超过后该次运行被拒绝。</summary>
    public long RunTokenLimit { get; set; } = 1_000_000;

    // 当前 Provider 没有硬费用计量认证；配置硬上限时明确拒绝，不把未知价格视作零。
    /// <summary>月度费用硬上限；为 null 表示不按费用限额，配置后超过即拒绝请求。</summary>
    public decimal? MonthlyCostLimit { get; set; }

    /// <summary>单次运行费用硬上限；为 null 表示不按费用限额，配置后超过即拒绝请求。</summary>
    public decimal? RunCostLimit { get; set; }

    /// <summary>费用限额使用的货币代码；默认 "USD"。</summary>
    public string Currency { get; set; } = "USD";
}
