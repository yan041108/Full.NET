using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>PDF 激活复用固定锁策略，补充成功结算时点与有界单批原载荷查询。</summary>
internal static class AiKnowledgeIndexPdfActivationSql
{
    private static string Settled(SqlStatement basis) => basis.Text.Replace("ledger.Outcome='succeeded'", "ledger.Outcome='succeeded' AND ledger.SettledAtUtc IS NOT NULL", StringComparison.Ordinal);
    private static string Payload(SqlStatement basis, bool sqlServer)
    {
        var text = Settled(basis);
        // 投影固定列名，沿用静态 AOT 行读取器；只物化一个有界批次。
        var from = text.IndexOf(" FROM ", StringComparison.Ordinal);
        var projection = "batch.Id,batch.IndexTaskId,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,task.ModelConfigId,task.ModelConfigVersion,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey,vector.PayloadHash,vector.Payload,vector.CreatedAtUtc";
        text = "SELECT " + (sqlServer ? "TOP (1) " : "") + projection + text[from..];
        var order = text.IndexOf(" ORDER BY ", StringComparison.Ordinal);
        text = text[..order] + " AND batch.BatchOrdinal=@BatchOrdinal AND " + (sqlServer ? "DATALENGTH" : "OCTET_LENGTH")
            + "(vector.Payload) BETWEEN 8 AND 524288" + (sqlServer ? "" : " LIMIT 1 FOR UPDATE");
        return text;
    }
    // 语句只在固定字段初始化时构造；帮助方法仅组合文本，避免运行时克隆逃离精确目录。
    internal static readonly SqlStatement ReceiptsSqlServerHost = new("ai.knowledge_pdf_activation.receipts_sqlserver_host", Settled(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsSqlServerTenant = new("ai.knowledge_pdf_activation.receipts_sqlserver_tenant", Settled(AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReceiptsMySqlHost = new("ai.knowledge_pdf_activation.receipts_mysql_host", Settled(AiKnowledgeIndexActivationSql.ReceiptsMySqlHost), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsMySqlTenant = new("ai.knowledge_pdf_activation.receipts_mysql_tenant", Settled(AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PayloadSqlServerHost = new("ai.knowledge_pdf_activation.payload_sqlserver_host", Payload(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost, true), SqlDataScope.HostOnly);
    internal static readonly SqlStatement PayloadSqlServerTenant = new("ai.knowledge_pdf_activation.payload_sqlserver_tenant", Payload(AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PayloadMySqlHost = new("ai.knowledge_pdf_activation.payload_mysql_host", Payload(AiKnowledgeIndexActivationSql.ReceiptsMySqlHost, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement PayloadMySqlTenant = new("ai.knowledge_pdf_activation.payload_mysql_tenant", Payload(AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
