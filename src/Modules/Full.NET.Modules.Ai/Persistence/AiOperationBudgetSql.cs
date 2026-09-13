using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>可信 scope 编码隔离 Host/租户，短事务持有 scope 行锁后才读写操作账本。</summary>
internal static class AiOperationBudgetSql
{
    public static readonly SqlStatement EnsureSqlServer = new("ai.ensure_budget_scope.sqlserver", """
        IF NOT EXISTS (SELECT 1 FROM fn_ai_budget_scope WITH (UPDLOCK, HOLDLOCK) WHERE ScopeKey = @ScopeKey)
            INSERT INTO fn_ai_budget_scope (Id, ScopeKey) VALUES (@ScopeId, @ScopeKey);
        UPDATE fn_ai_budget_scope SET ScopeKey = @ScopeKey WHERE ScopeKey = @ScopeKey;
        """, SqlDataScope.Global);
    public static readonly SqlStatement EnsureMySql = new("ai.ensure_budget_scope.mysql", """
        INSERT INTO fn_ai_budget_scope (Id, ScopeKey) VALUES (@ScopeId, @ScopeKey)
        ON DUPLICATE KEY UPDATE ScopeKey = @ScopeKey;
        """, SqlDataScope.Global);
    public static readonly SqlStatement Find = new("ai.find_budget_operation", """
        SELECT Id, RunId, ModelConfigId, RequestHash, QuotaMonthKey, ReservedTokens, ReservedCost,
            Currency, PriceVersionId, InputPerMillion, OutputPerMillion, CachedInputPerMillion,
            InputTokens, OutputTokens, CachedInputTokens, UsageStatus, Outcome, LegacyTracked
        FROM fn_ai_operation_budget WHERE Id = @OperationId AND ScopeKey = @ScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Totals = new("ai.sum_budget_operations", """
        SELECT
            COALESCE(SUM(CASE WHEN QuotaMonthKey = @QuotaMonthKey THEN CAST(1 AS decimal(19,0)) ELSE 0 END), 0) AS MonthlyRequests,
            COALESCE(SUM(CASE WHEN QuotaMonthKey = @QuotaMonthKey THEN ChargedTokens ELSE 0 END), 0) AS MonthlyTokens,
            COALESCE(SUM(CASE WHEN RunId = @RunId THEN CAST(1 AS decimal(19,0)) ELSE 0 END), 0) AS RunRequests,
            COALESCE(SUM(CASE WHEN RunId = @RunId THEN ChargedTokens ELSE 0 END), 0) AS RunTokens
        FROM fn_ai_operation_budget WHERE ScopeKey = @ScopeKey AND (QuotaMonthKey = @QuotaMonthKey OR RunId = @RunId)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("ai.insert_budget_operation", """
        INSERT INTO fn_ai_operation_budget
            (Id, ScopeKey, RunId, ModelConfigId, ProviderKey, ModelId, RequestHash, QuotaMonthKey, ReservedTokens, ReservedCost,
             Currency, PriceVersionId, InputPerMillion, OutputPerMillion, CachedInputPerMillion,
             ChargedTokens, ChargedCost, UsageStatus, Outcome, LegacyTracked, CreatedAtUtc, TraceId)
        VALUES (@OperationId, @ScopeKey, @RunId, @ModelConfigId, @ProviderKey, @ModelId, @RequestHash, @QuotaMonthKey, @ReservedTokens, @ReservedCost,
             @Currency, @PriceVersionId, @InputPerMillion, @OutputPerMillion, @CachedInputPerMillion,
             @ReservedTokens, @ReservedCost, 'reserved', 'pending', @LegacyTracked, @Now, @TraceId)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Settle = new("ai.settle_budget_operation", """
        UPDATE fn_ai_operation_budget
        SET UsageStatus = @UsageStatus, Outcome = @Outcome, InputTokens = @InputTokens, OutputTokens = @OutputTokens,
            CachedInputTokens = @CachedInputTokens, ChargedTokens = @ChargedTokens, ChargedCost = @ChargedCost, SettledAtUtc = @Now
        WHERE Id = @OperationId AND ScopeKey = @ScopeKey AND UsageStatus IN ('reserved', 'unknown')
        """, SqlDataScope.Global);
}
