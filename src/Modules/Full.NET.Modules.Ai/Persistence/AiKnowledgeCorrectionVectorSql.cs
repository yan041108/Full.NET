using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>人工批次沿用完整清单、成功结算、实际字节摘要及原会话守卫；格式只由固定服务端映射替换。</summary>
internal static class AiKnowledgeCorrectionVectorSql
{
    private static string Map(SqlStatement source, string name)
    {
        var sql = source.Text.Replace("knowledge-document-manifest-v1", "knowledge-correction-manifest-v1", StringComparison.Ordinal)
            .Replace("knowledge-document-vector-batch-v1", "knowledge-correction-vector-v1", StringComparison.Ordinal)
            .Replace("OCTET_LENGTH(manifest.FormatKey)=30", "OCTET_LENGTH(manifest.FormatKey)=32", StringComparison.Ordinal)
            .Replace("DATALENGTH(manifest.FormatKey)=30", "DATALENGTH(manifest.FormatKey)=32", StringComparison.Ordinal)
            .Replace("OCTET_LENGTH(@FormatKey)=34", "OCTET_LENGTH(@FormatKey)=30", StringComparison.Ordinal)
            .Replace("DATALENGTH(CONVERT(varchar(64),@FormatKey))=34", "DATALENGTH(CONVERT(varchar(64),@FormatKey))=30", StringComparison.Ordinal)
            .Replace("OCTET_LENGTH(vector.FormatKey)=34", "OCTET_LENGTH(vector.FormatKey)=30", StringComparison.Ordinal)
            .Replace("DATALENGTH(vector.FormatKey)=34", "DATALENGTH(vector.FormatKey)=30", StringComparison.Ordinal);
        const string guard = " AND fn_ai_knowledge_index_task.CorrectionRevisionId IS NOT NULL";
        var order = sql.IndexOf(" ORDER BY ", StringComparison.Ordinal);
        sql = order >= 0 ? sql[..order] + guard + sql[order..]
            : sql.EndsWith(" FOR SHARE", StringComparison.Ordinal) ? sql[..^10] + guard + " FOR SHARE" : sql + guard;
        if (name.Contains(".find_", StringComparison.Ordinal))
        {
            // 在数据库读取点复算实际载荷摘要并限幅，不把表中自报摘要当作原字节证明。
            sql += sql.Contains("DATALENGTH(", StringComparison.Ordinal)
                ? " AND DATALENGTH(vector.Payload) BETWEEN 8 AND 524288 AND vector.PayloadHash COLLATE Latin1_General_100_BIN2=CONVERT(varchar(64),HASHBYTES('SHA2_256',vector.Payload),2)"
                : " AND OCTET_LENGTH(vector.Payload) BETWEEN 8 AND 524288 AND CAST(vector.PayloadHash AS BINARY)=CAST(UPPER(SHA2(vector.Payload,256)) AS BINARY)";
        }
        return sql;
    }

    // 原始批次数也必须精确匹配，不能用成功 JOIN 隐藏未付费或未保存的额外批次。
    internal static readonly SqlStatement ReceiptsMySqlHost = new("ai.knowledge_correction_vector.receipts_mysql_host", Map(AiKnowledgeIndexDocumentVectorSql.ReceiptsDocumentHost, "ai.knowledge_correction_vector.receipts_mysql_host"), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsMySqlTenant = new("ai.knowledge_correction_vector.receipts_mysql_tenant", Map(AiKnowledgeIndexDocumentVectorSql.ReceiptsDocumentTenant, "ai.knowledge_correction_vector.receipts_mysql_tenant"), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReceiptsSqlServerHost = new("ai.knowledge_correction_vector.receipts_sqlserver_host", Map(AiKnowledgeIndexDocumentVectorSql.ReceiptsDocumentSqlServerHost, "ai.knowledge_correction_vector.receipts_sqlserver_host"), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsSqlServerTenant = new("ai.knowledge_correction_vector.receipts_sqlserver_tenant", Map(AiKnowledgeIndexDocumentVectorSql.ReceiptsDocumentSqlServerTenant, "ai.knowledge_correction_vector.receipts_sqlserver_tenant"), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertMySqlHost = new("ai.knowledge_correction_vector.insert_mysql_host", Map(AiKnowledgeIndexDocumentVectorSql.InsertDocumentHost, "ai.knowledge_correction_vector.insert_mysql_host"), SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertMySqlTenant = new("ai.knowledge_correction_vector.insert_mysql_tenant", Map(AiKnowledgeIndexDocumentVectorSql.InsertDocumentTenant, "ai.knowledge_correction_vector.insert_mysql_tenant"), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertSqlServerHost = new("ai.knowledge_correction_vector.insert_sqlserver_host", Map(AiKnowledgeIndexDocumentVectorSql.InsertDocumentSqlServerHost, "ai.knowledge_correction_vector.insert_sqlserver_host"), SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertSqlServerTenant = new("ai.knowledge_correction_vector.insert_sqlserver_tenant", Map(AiKnowledgeIndexDocumentVectorSql.InsertDocumentSqlServerTenant, "ai.knowledge_correction_vector.insert_sqlserver_tenant"), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindMySqlHost = new("ai.knowledge_correction_vector.find_mysql_host", Map(AiKnowledgeIndexDocumentVectorSql.FindDocumentHost, "ai.knowledge_correction_vector.find_mysql_host"), SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindMySqlTenant = new("ai.knowledge_correction_vector.find_mysql_tenant", Map(AiKnowledgeIndexDocumentVectorSql.FindDocumentTenant, "ai.knowledge_correction_vector.find_mysql_tenant"), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindSqlServerHost = new("ai.knowledge_correction_vector.find_sqlserver_host", Map(AiKnowledgeIndexDocumentVectorSql.FindDocumentSqlServerHost, "ai.knowledge_correction_vector.find_sqlserver_host"), SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindSqlServerTenant = new("ai.knowledge_correction_vector.find_sqlserver_tenant", Map(AiKnowledgeIndexDocumentVectorSql.FindDocumentSqlServerTenant, "ai.knowledge_correction_vector.find_sqlserver_tenant"), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement Write(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? InsertMySqlTenant : InsertMySqlHost : tenant ? InsertSqlServerTenant : InsertSqlServerHost;
    internal static SqlStatement Read(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? FindMySqlTenant : FindMySqlHost : tenant ? FindSqlServerTenant : FindSqlServerHost;
    internal static SqlStatement Receipts(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? ReceiptsMySqlTenant : ReceiptsMySqlHost : tenant ? ReceiptsSqlServerTenant : ReceiptsSqlServerHost;
}
