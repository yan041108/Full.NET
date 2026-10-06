using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>PDF 激活复用固定锁策略，补充成功结算时点与有界单批原载荷查询。</summary>
internal static class AiKnowledgeIndexPdfActivationSql
{
    private static string Settled(SqlStatement basis) => basis.Text.Replace("ledger.Outcome='succeeded'", "ledger.Outcome='succeeded' AND ledger.SettledAtUtc IS NOT NULL", StringComparison.Ordinal);
    private static SqlStatement Receipts(SqlStatement basis, string name) => new(name, Settled(basis), basis.Scope, basis.TenantBinding);
    private static SqlStatement Payload(SqlStatement basis, string name, bool sqlServer)
    {
        var text = Settled(basis);
        // 投影固定列名，沿用静态 AOT 行读取器；只物化一个有界批次。
        var from = text.IndexOf(" FROM ", StringComparison.Ordinal);
        var projection = "batch.Id,batch.IndexTaskId,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,task.ModelConfigId,task.ModelConfigVersion,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey,vector.PayloadHash,vector.Payload,vector.CreatedAtUtc";
        text = "SELECT " + (sqlServer ? "TOP (1) " : "") + projection + text[from..];
        var order = text.IndexOf(" ORDER BY ", StringComparison.Ordinal);
        text = text[..order] + " AND batch.BatchOrdinal=@BatchOrdinal AND " + (sqlServer ? "DATALENGTH" : "OCTET_LENGTH")
            + "(vector.Payload) BETWEEN 8 AND 524288" + (sqlServer ? "" : " LIMIT 1 FOR UPDATE");
        return new(name, text, basis.Scope, basis.TenantBinding);
    }
    internal static readonly SqlStatement ReceiptsSqlServerHost = Receipts(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost, "ai.knowledge_pdf_activation.receipts_sqlserver_host");
    internal static readonly SqlStatement ReceiptsSqlServerTenant = Receipts(AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant, "ai.knowledge_pdf_activation.receipts_sqlserver_tenant");
    internal static readonly SqlStatement ReceiptsMySqlHost = Receipts(AiKnowledgeIndexActivationSql.ReceiptsMySqlHost, "ai.knowledge_pdf_activation.receipts_mysql_host");
    internal static readonly SqlStatement ReceiptsMySqlTenant = Receipts(AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant, "ai.knowledge_pdf_activation.receipts_mysql_tenant");
    internal static readonly SqlStatement PayloadSqlServerHost = Payload(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost, "ai.knowledge_pdf_activation.payload_sqlserver_host", true);
    internal static readonly SqlStatement PayloadSqlServerTenant = Payload(AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant, "ai.knowledge_pdf_activation.payload_sqlserver_tenant", true);
    internal static readonly SqlStatement PayloadMySqlHost = Payload(AiKnowledgeIndexActivationSql.ReceiptsMySqlHost, "ai.knowledge_pdf_activation.payload_mysql_host", false);
    internal static readonly SqlStatement PayloadMySqlTenant = Payload(AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant, "ai.knowledge_pdf_activation.payload_mysql_tenant", false);
}
