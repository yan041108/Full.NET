namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 租户配额持久化行。</summary>
internal sealed class AiTenantQuotaRecord
{
    /// <summary>配额记录标识。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识。</summary>
    public Guid TenantId { get; init; }

    /// <summary>每月 Token 上限。</summary>
    public long? MonthlyTokenLimit { get; init; }

    /// <summary>每月请求次数上限。</summary>
    public long? MonthlyRequestLimit { get; init; }

    /// <summary>本月已用 Token。</summary>
    public long UsedTokensThisMonth { get; init; }

    /// <summary>本月已用请求次数。</summary>
    public long UsedRequestsThisMonth { get; init; }

    /// <summary>配额月份键。</summary>
    public string QuotaMonthKey { get; init; } = string.Empty;

    /// <summary>是否启用配额。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
}
