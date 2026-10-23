using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>多格式文档激活复用固定锁策略，补充成功结算时点与有界单批原载荷查询。</summary>
internal static class AiKnowledgeIndexDocumentActivationSql
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
    internal static readonly SqlStatement ReceiptsSqlServerHost = new("ai.knowledge_document_activation.receipts_sqlserver_host", Settled(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsSqlServerTenant = new("ai.knowledge_document_activation.receipts_sqlserver_tenant", Settled(AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReceiptsMySqlHost = new("ai.knowledge_document_activation.receipts_mysql_host", Settled(AiKnowledgeIndexActivationSql.ReceiptsMySqlHost), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsMySqlTenant = new("ai.knowledge_document_activation.receipts_mysql_tenant", Settled(AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PayloadSqlServerHost = new("ai.knowledge_document_activation.payload_sqlserver_host", Payload(AiKnowledgeIndexActivationSql.ReceiptsSqlServerHost, true), SqlDataScope.HostOnly);
    internal static readonly SqlStatement PayloadSqlServerTenant = new("ai.knowledge_document_activation.payload_sqlserver_tenant", Payload(AiKnowledgeIndexActivationSql.ReceiptsSqlServerTenant, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PayloadMySqlHost = new("ai.knowledge_document_activation.payload_mysql_host", Payload(AiKnowledgeIndexActivationSql.ReceiptsMySqlHost, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement PayloadMySqlTenant = new("ai.knowledge_document_activation.payload_mysql_tenant", Payload(AiKnowledgeIndexActivationSql.ReceiptsMySqlTenant, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static string Manifest(SqlStatement basis) => basis.Text.Replace("fn_ai_knowledge_text_manifest", "fn_ai_knowledge_document_manifest", StringComparison.Ordinal);
    internal static readonly SqlStatement ManifestHost = new("ai.knowledge_document_activation.manifest_host", Manifest(AiKnowledgeIndexActivationSql.ManifestHost), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestTenant = new("ai.knowledge_document_activation.manifest_tenant", Manifest(AiKnowledgeIndexActivationSql.ManifestTenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement AuthoritySqlServerHost = new("ai.knowledge_document_activation.authority_sqlserver_host", Manifest(AiKnowledgeIndexActivationSql.AuthoritySqlServerHost) + " AND version.SizeBytes=@SizeBytes AND version.ParsingPolicyKey=@ParsingPolicyKey", SqlDataScope.HostOnly);
    internal static readonly SqlStatement AuthoritySqlServerTenant = new("ai.knowledge_document_activation.authority_sqlserver_tenant", Manifest(AiKnowledgeIndexActivationSql.AuthoritySqlServerTenant) + " AND version.SizeBytes=@SizeBytes AND version.ParsingPolicyKey=@ParsingPolicyKey", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement AuthorityMySqlHost = new("ai.knowledge_document_activation.authority_mysql_host", Manifest(AiKnowledgeIndexActivationSql.AuthorityMySqlHost).Replace(" FOR UPDATE", " AND version.SizeBytes=@SizeBytes AND version.ParsingPolicyKey=@ParsingPolicyKey FOR UPDATE", StringComparison.Ordinal), SqlDataScope.HostOnly);
    internal static readonly SqlStatement AuthorityMySqlTenant = new("ai.knowledge_document_activation.authority_mysql_tenant", Manifest(AiKnowledgeIndexActivationSql.AuthorityMySqlTenant).Replace(" FOR UPDATE", " AND version.SizeBytes=@SizeBytes AND version.ParsingPolicyKey=@ParsingPolicyKey FOR UPDATE", StringComparison.Ordinal), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
