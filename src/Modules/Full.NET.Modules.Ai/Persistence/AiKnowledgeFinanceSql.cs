using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>范围财务按原scope、月份、价格和币种聚合；历史缺少显式运行关联的行保持未归属。</summary>
internal static class AiKnowledgeFinanceSql
{
    private static string Text(bool mysql)=>"SELECT "+(mysql?"":"TOP (201) ")
        +"operation.KnowledgeBaseId,ledger.QuotaMonthKey AS Month,ledger.PriceVersionId,ledger.Currency,COUNT(*) AS Requests,"
        +"SUM(CASE WHEN ledger.UsageStatus='known' THEN 1 ELSE 0 END) AS Known,"
        +"SUM(CASE WHEN ledger.UsageStatus='unknown' THEN 1 ELSE 0 END) AS Unknown,"
        +"SUM(CASE WHEN ledger.UsageStatus='reserved' THEN 1 ELSE 0 END) AS Reserved,"
        +"SUM(CASE WHEN ledger.UsageStatus<>'known' OR ledger.ChargedCost IS NULL OR ledger.SettledAtUtc IS NULL THEN 1 ELSE 0 END) AS MissingCost,"
        +"SUM(CASE WHEN ledger.UsageStatus='known' AND ledger.SettledAtUtc IS NOT NULL THEN ledger.ChargedCost ELSE NULL END) AS RecordedCost,"
        +"SUM(CASE WHEN ledger.UsageStatus IN ('reserved','unknown') THEN ledger.ReservedCost ELSE NULL END) AS ConservativeReservation"
        +" FROM fn_ai_operation_budget AS ledger LEFT JOIN fn_ai_knowledge_evaluation_operation AS operation ON operation.Id=ledger.Id AND "
        +"((operation.TenantId IS NULL AND @ScopeKey='host') OR operation.TenantId=@ScopeTenantId)"
        +" WHERE ledger.ScopeKey=@ScopeKey AND ledger.CreatedAtUtc>=@FromUtc AND ledger.CreatedAtUtc<@ToUtc"
        +" AND ledger.QuotaMonthKey>=@FromMonth AND ledger.QuotaMonthKey<=@ToMonth"
        +" AND (@KnowledgeBaseId IS NULL OR operation.KnowledgeBaseId=@KnowledgeBaseId)"
        +" GROUP BY operation.KnowledgeBaseId,ledger.QuotaMonthKey,ledger.PriceVersionId,ledger.Currency"
        +" ORDER BY operation.KnowledgeBaseId,ledger.QuotaMonthKey,ledger.PriceVersionId,ledger.Currency"+(mysql?" LIMIT 201":"");
    internal static readonly SqlStatement ReadSqlServer=new("ai.knowledge_finance.read_sqlserver",Text(false),SqlDataScope.Global);
    internal static readonly SqlStatement ReadMySql=new("ai.knowledge_finance.read_mysql",Text(true),SqlDataScope.Global);
    internal static SqlStatement ReadFor(DatabaseProvider provider)=>provider==DatabaseProvider.MySql?ReadMySql:ReadSqlServer;
}

/// <summary>原账本分组的静态投影；金额可空，保守预留不冒充已知费用。</summary>
internal sealed record AiKnowledgeFinanceRow
{
    public Guid? KnowledgeBaseId {get;init;}
    public string Month {get;init;}=string.Empty;
    public Guid? PriceVersionId {get;init;}
    public string? Currency {get;init;}
    public long Requests {get;init;}
    public long Known {get;init;}
    public long Unknown {get;init;}
    public long Reserved {get;init;}
    public long MissingCost {get;init;}
    public decimal? RecordedCost {get;init;}
    public decimal? ConservativeReservation {get;init;}
}
