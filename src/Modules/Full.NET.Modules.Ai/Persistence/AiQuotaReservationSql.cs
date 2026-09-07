using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>租户配额原子预留与幂等结算；月份只能向前推进，取消和崩溃不释放未知用量。</summary>
internal static class AiQuotaReservationSql
{
    /// <summary>单次 UPDATE 同时检查并占用请求和 Token；月份最后赋值以兼容 MySQL 左到右赋值。</summary>
    public static readonly SqlStatement Reserve = new(
        "ai.reserve_quota_usage",
        """
        UPDATE fn_ai_tenant_quota
        SET UsedTokensThisMonth = CASE WHEN QuotaMonthKey = @QuotaMonthKey THEN UsedTokensThisMonth ELSE 0 END + @ReservedTokens,
            UsedRequestsThisMonth = CASE WHEN QuotaMonthKey = @QuotaMonthKey THEN UsedRequestsThisMonth ELSE 0 END + 1,
            QuotaMonthKey = @QuotaMonthKey,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId AND IsEnabled = 1 AND QuotaMonthKey <= @QuotaMonthKey
          AND (MonthlyRequestLimit IS NULL OR
            CASE WHEN QuotaMonthKey = @QuotaMonthKey THEN UsedRequestsThisMonth ELSE 0 END < MonthlyRequestLimit)
          AND (MonthlyTokenLimit IS NULL OR
            CASE WHEN QuotaMonthKey = @QuotaMonthKey THEN UsedTokensThisMonth ELSE 0 END <= MonthlyTokenLimit - @ReservedTokens)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>在同一事务保存预留凭据，后续结算不能伪造月份或额度。</summary>
    public static readonly SqlStatement Insert = new(
        "ai.insert_quota_reservation",
        """
        INSERT INTO fn_ai_quota_reservation
            (Id, TenantId, QuotaMonthKey, ReservedTokens, IsSettled, CreatedAtUtc)
        VALUES (@Id, @TenantId, @QuotaMonthKey, @ReservedTokens, 0, @UpdatedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>领取唯一结算权；重复请求不再影响配额。</summary>
    public static readonly SqlStatement Settle = new(
        "ai.settle_quota_reservation",
        """
        UPDATE fn_ai_quota_reservation
        SET IsSettled = 1, ActualTokens = @ActualTokens, SettledAtUtc = @UpdatedAtUtc
        WHERE Id = @Id AND TenantId = @TenantId AND QuotaMonthKey = @QuotaMonthKey
          AND ReservedTokens = @ReservedTokens AND IsSettled = 0
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>只调整预留所属月份；上月迟到结算不能修改新月份的计数。</summary>
    public static readonly SqlStatement Adjust = new(
        "ai.adjust_reserved_quota_usage",
        """
        UPDATE fn_ai_tenant_quota
        SET UsedTokensThisMonth = UsedTokensThisMonth + @TokenDelta,
            UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE TenantId = @TenantId AND QuotaMonthKey = @QuotaMonthKey
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
