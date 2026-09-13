namespace Full.NET.Modules.Ai.Persistence;

/// <summary>操作账本凭据；价格快照和原月份不可在结算中替换。</summary>
internal sealed class AiOperationBudgetRecord
{
    public Guid Id { get; init; }
    public Guid? RunId { get; init; }
    public Guid ModelConfigId { get; init; }
    public string RequestHash { get; init; } = string.Empty;
    public string QuotaMonthKey { get; init; } = string.Empty;
    public long ReservedTokens { get; init; }
    public decimal? ReservedCost { get; init; }
    public string? Currency { get; init; }
    public Guid? PriceVersionId { get; init; }
    public decimal? InputPerMillion { get; init; }
    public decimal? OutputPerMillion { get; init; }
    public decimal? CachedInputPerMillion { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public string UsageStatus { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public bool LegacyTracked { get; init; }
}

/// <summary>在 scope 写锁下读取月度和 Run 聚合，不依赖本地进程计数。</summary>
internal sealed class AiOperationBudgetTotals
{
    public long MonthlyRequests { get; init; }
    public long MonthlyTokens { get; init; }
    public long RunRequests { get; init; }
    public long RunTokens { get; init; }
}
