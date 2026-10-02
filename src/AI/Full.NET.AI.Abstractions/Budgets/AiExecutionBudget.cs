namespace Full.NET.AI.Abstractions.Budgets;

/// <summary>模型价格快照；费率单位为每百万 Token，版本与币种随操作固化。</summary>
/// <param name="VersionId">价格快照版本标识；与执行预算的计费版本绑定，发布后不可改名。</param>
/// <param name="Currency">ISO 4217 三位大写币种码；长度必须为 3 且仅含 A-Z。</param>
/// <param name="InputPerMillion">每百万输入 Token 单价；不得为负。</param>
/// <param name="OutputPerMillion">每百万输出 Token 单价；不得为负。</param>
/// <param name="CachedInputPerMillion">每百万缓存输入 Token 单价；不得大于 InputPerMillion。</param>
public sealed record AiModelPrice(Guid VersionId, string Currency, decimal InputPerMillion,
    decimal OutputPerMillion, decimal CachedInputPerMillion);

/// <summary>完整输入含缓存输入；缺少缓存计量时按普通输入费率保守记账。</summary>
/// <param name="InputTokens">输入 Token 总数（含缓存）；为空表示缺失计量，按无法计费处理。</param>
/// <param name="OutputTokens">输出 Token 总数；为空表示缺失计量，按无法计费处理。</param>
/// <param name="CachedInputTokens">缓存命中的输入 Token 数；不得超过 InputTokens，缺失时按 0 保守记账。</param>
public sealed record AiOperationUsage(long? InputTokens, long? OutputTokens, long? CachedInputTokens = null);

/// <summary>由已授权调用编排构造，摘要必须为完整请求的 SHA-256；租户不由请求传入。</summary>
/// <param name="OperationId">操作标识；与预留凭据一一对应，发布后不可改名。</param>
/// <param name="RunId">关联运行标识；可空表示尚未挂接到运行实例。</param>
/// <param name="ModelConfigId">已授权的模型配置标识；计费快照据此加载。</param>
/// <param name="Kind">操作种类稳定键；决定计量与计费路径。</param>
/// <param name="RequestHash">完整请求的 SHA-256 摘要；用于幂等与对账，不可省略。</param>
/// <param name="InputTokenLimit">本次操作输入 Token 上限；超出按硬上限拒绝。</param>
/// <param name="OutputTokenLimit">本次操作输出 Token 上限；超出按硬上限拒绝。</param>
/// <param name="RequireHardCostLimit">是否要求费用硬上限；true 时超额立即拒绝而非仅告警。</param>
/// <param name="ProviderKey">底层 Provider 稳定键；空字符串表示由编排层按配置解析。</param>
/// <param name="ModelId">底层模型稳定标识；空字符串表示由编排层按配置解析。</param>
public sealed record AiOperationRequest(Guid OperationId, Guid? RunId, Guid ModelConfigId,
    string Kind, string RequestHash, long InputTokenLimit, long OutputTokenLimit, bool RequireHardCostLimit = false, string ProviderKey = "", string ModelId = "");

/// <summary>重复预留只返回原凭据，IsNew=false 不授权再次派发模型。</summary>
/// <param name="OperationId">操作标识；与请求一一对应。</param>
/// <param name="IsNew">是否本次新建预留；false 表示命中已有凭据，禁止再次派发模型。</param>
/// <param name="UsageStatus">用量状态稳定键；决定后续计费或回收路径。</param>
/// <param name="ReservedTokens">本次预留 Token 总量上限；超额触发拒绝或告警。</param>
/// <param name="ReservedCost">本次预留费用上限；为空表示尚未计价。</param>
/// <param name="Currency">预留费用币种；与 ReservedCost 同时返回，为空表示未计价。</param>
public sealed record AiOperationReservation(Guid OperationId, bool IsNew, string UsageStatus,
    long ReservedTokens, decimal? ReservedCost, string? Currency);

/// <summary>稳定业务错误码；异常正文不包含请求、凭据或底层数据库信息。</summary>
/// <param name="code">稳定错误码；发布后不可改名或删除，调用方据此分支处理。</param>
public sealed class AiBudgetException(string code) : Exception("AI operation budget rejected.")
{
    /// <summary>
    /// 稳定错误码；发布后不可改名或删除，调用方据此分支处理预算拒绝原因。
    /// </summary>
    public string Code { get; } = code;
}

/// <summary>费用采用 decimal 和向上舍入，非法输入在任何持久化变更前拒绝。</summary>
/// <remarks>
/// 常量字符串发布后不可改名或删除；新增常量只能追加。
/// </remarks>
public static class AiExecutionBudget
{
    /// <summary>单次操作允许的最大 Token 数（输入或输出）；超过此上限按非法用量拒绝，取值为 1,000,000,000。</summary>
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
