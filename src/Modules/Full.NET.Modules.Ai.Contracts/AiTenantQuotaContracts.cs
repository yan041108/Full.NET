namespace Full.NET.Modules.Ai.Contracts;

/// <summary>AI 租户配额列表项。</summary>
/// <param name="Id">配额记录标识。</param>
/// <param name="TenantId">租户标识。</param>
/// <param name="MonthlyTokenLimit">每月 Token 上限；为空表示不限制。</param>
/// <param name="MonthlyRequestLimit">每月请求次数上限；为空表示不限制。</param>
/// <param name="UsedTokensThisMonth">本月已用 Token 数。</param>
/// <param name="UsedRequestsThisMonth">本月已用请求次数。</param>
/// <param name="QuotaMonthKey">配额统计月份键（yyyy-MM）。</param>
/// <param name="IsEnabled">是否启用配额限制。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AiTenantQuotaListItem(
    Guid Id,
    Guid TenantId,
    long? MonthlyTokenLimit,
    long? MonthlyRequestLimit,
    long UsedTokensThisMonth,
    long UsedRequestsThisMonth,
    string QuotaMonthKey,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>AI 租户配额详情。</summary>
/// <param name="Id">配额记录标识。</param>
/// <param name="TenantId">租户标识。</param>
/// <param name="MonthlyTokenLimit">每月 Token 上限；为空表示不限制。</param>
/// <param name="MonthlyRequestLimit">每月请求次数上限；为空表示不限制。</param>
/// <param name="UsedTokensThisMonth">本月已用 Token 数。</param>
/// <param name="UsedRequestsThisMonth">本月已用请求次数。</param>
/// <param name="QuotaMonthKey">配额统计月份键（yyyy-MM）。</param>
/// <param name="IsEnabled">是否启用配额限制。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AiTenantQuotaResponse(
    Guid Id,
    Guid TenantId,
    long? MonthlyTokenLimit,
    long? MonthlyRequestLimit,
    long UsedTokensThisMonth,
    long UsedRequestsThisMonth,
    string QuotaMonthKey,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>更新 AI 租户配额请求。</summary>
/// <param name="MonthlyTokenLimit">每月 Token 上限；为空表示不限制。</param>
/// <param name="MonthlyRequestLimit">每月请求次数上限；为空表示不限制。</param>
/// <param name="IsEnabled">是否启用配额限制。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record UpdateAiTenantQuotaRequest(
    long? MonthlyTokenLimit,
    long? MonthlyRequestLimit,
    bool IsEnabled,
    int Version);
