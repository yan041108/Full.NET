using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 租户配额管理 SQL。</summary>
internal static class AiTenantQuotaSql
{
    private const string SelectColumns = """
        quota.Id,
               quota.TenantId,
               quota.MonthlyTokenLimit,
               quota.MonthlyRequestLimit,
               quota.UsedTokensThisMonth,
               quota.UsedRequestsThisMonth,
               quota.QuotaMonthKey,
               quota.IsEnabled,
               quota.CreatedAtUtc,
               quota.UpdatedAtUtc,
               quota.Version
        """;

    public static readonly SqlStatement Insert = new(
        "ai.insert_tenant_quota",
        """
        INSERT INTO fn_ai_tenant_quota
            (Id, TenantId, MonthlyTokenLimit, MonthlyRequestLimit, UsedTokensThisMonth,
             UsedRequestsThisMonth, QuotaMonthKey, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @MonthlyTokenLimit, @MonthlyRequestLimit, @UsedTokensThisMonth,
             @UsedRequestsThisMonth, @QuotaMonthKey, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByTenantId = new(
        "ai.find_tenant_quota_by_tenant_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_ai_tenant_quota AS quota
        WHERE quota.TenantId = @TenantId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "ai.update_tenant_quota",
        """
        UPDATE fn_ai_tenant_quota
        SET MonthlyTokenLimit = @MonthlyTokenLimit,
            MonthlyRequestLimit = @MonthlyRequestLimit,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement IncrementUsage = new(
        "ai.increment_tenant_quota_usage",
        """
        UPDATE fn_ai_tenant_quota
        SET UsedTokensThisMonth = UsedTokensThisMonth + @TokenDelta,
            UsedRequestsThisMonth = UsedRequestsThisMonth + @RequestDelta,
            QuotaMonthKey = @QuotaMonthKey,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ResetMonthlyUsage = new(
        "ai.reset_tenant_quota_monthly_usage",
        """
        UPDATE fn_ai_tenant_quota
        SET UsedTokensThisMonth = 0,
            UsedRequestsThisMonth = 0,
            QuotaMonthKey = @QuotaMonthKey,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId
          AND QuotaMonthKey <> @QuotaMonthKey
        """,
        SqlDataScope.HostOnly);

    public static readonly string CountSqlServer = """
        SELECT COUNT(1)
        FROM fn_ai_tenant_quota AS quota
        WHERE (@TenantId IS NULL OR quota.TenantId = @TenantId)
        """;

    public static readonly string ListSqlServer = $"""
        SELECT {SelectColumns}
        FROM fn_ai_tenant_quota AS quota
        WHERE (@TenantId IS NULL OR quota.TenantId = @TenantId)
        ORDER BY quota.TenantId, quota.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountMySql = """
        SELECT COUNT(1)
        FROM fn_ai_tenant_quota AS quota
        WHERE (@TenantId IS NULL OR quota.TenantId = @TenantId)
        """;

    public static readonly string ListMySql = $"""
        SELECT {SelectColumns}
        FROM fn_ai_tenant_quota AS quota
        WHERE (@TenantId IS NULL OR quota.TenantId = @TenantId)
        ORDER BY quota.TenantId, quota.Id
        LIMIT @PageSize OFFSET @Offset
        """;
}
