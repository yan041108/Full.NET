namespace Full.NET.AI.Abstractions.Budgets;

/// <summary>模型价格快照；费率单位为每百万 Token，版本与币种随操作固化。</summary>
public sealed record AiModelPrice(Guid VersionId, string Currency, decimal InputPerMillion,
    decimal OutputPerMillion, decimal CachedInputPerMillion);

/// <summary>完整输入含缓存输入；缺少缓存计量时按普通输入费率保守记账。</summary>
public sealed record AiOperationUsage(long? InputTokens, long? OutputTokens, long? CachedInputTokens = null);

/// <summary>由已授权调用编排构造，摘要必须为完整请求的 SHA-256；租户不由请求传入。</summary>
public sealed record AiOperationRequest(Guid OperationId, Guid? RunId, Guid ModelConfigId,
    string Kind, string RequestHash, long InputTokenLimit, long OutputTokenLimit, bool RequireHardCostLimit = false, string ProviderKey = "", string ModelId = "");

/// <summary>重复预留只返回原凭据，IsNew=false 不授权再次派发模型。</summary>
public sealed record AiOperationReservation(Guid OperationId, bool IsNew, string UsageStatus,
    long ReservedTokens, decimal? ReservedCost, string? Currency);

/// <summary>稳定业务错误码；异常正文不包含请求、凭据或底层数据库信息。</summary>
public sealed class AiBudgetException(string code) : Exception("AI operation budget rejected.")
{
    public string Code { get; } = code;
}

/// <summary>费用采用 decimal 和向上舍入，非法输入在任何持久化变更前拒绝。</summary>
public static class AiExecutionBudget
{
    public const long MaximumTokens = 1_000_000_000;

    public static void ValidateUsage(AiOperationUsage usage)
    {
        if (usage.InputTokens is < 0 or > MaximumTokens || usage.OutputTokens is < 0 or > MaximumTokens
            || usage.CachedInputTokens is < 0 || usage.CachedInputTokens > usage.InputTokens
            || (usage.CachedInputTokens.HasValue && !usage.InputTokens.HasValue))
            throw new AiBudgetException("ai.budget.invalid_usage");
    }

    public static decimal? CalculateCost(AiModelPrice? price, AiOperationUsage usage)
    {
        ValidateUsage(usage);
        if (price is null) return null;
        if (price.VersionId == Guid.Empty || price.Currency.Length != 3 || price.Currency.Any(c => c is < 'A' or > 'Z')
            || price.InputPerMillion is < 0 or > 1_000_000 || price.OutputPerMillion is < 0 or > 1_000_000
            || price.CachedInputPerMillion < 0 || price.CachedInputPerMillion > price.InputPerMillion)
            throw new AiBudgetException("ai.budget.invalid_price");
        if (usage.InputTokens is not { } input || usage.OutputTokens is not { } output) return null;
        var cached = usage.CachedInputTokens ?? 0;
        var amount = ((input - cached) * price.InputPerMillion + cached * price.CachedInputPerMillion
            + output * price.OutputPerMillion) / 1_000_000m;
        return decimal.Ceiling(amount * 100_000_000m) / 100_000_000m;
    }
}
