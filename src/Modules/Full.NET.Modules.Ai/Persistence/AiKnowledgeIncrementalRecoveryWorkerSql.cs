using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>全局只扫描 AI 自有最小来源锚点；文档排序由数据库决定，每条写入另建可信范围重验。</summary>
internal static class AiKnowledgeIncrementalRecoveryWorkerSql
{
    private const string Columns = "knowledge.Id AS KnowledgeBaseId, document.Id AS DocumentId, version.Id AS VersionId, knowledge.TenantId";
    private const string Scan = " FROM fn_ai_knowledge_base AS knowledge INNER JOIN fn_ai_knowledge_document AS document ON document.KnowledgeBaseId=knowledge.Id INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id INNER JOIN fn_ai_knowledge_import AS operation ON operation.VersionId=version.Id AND operation.DocumentId=document.Id LEFT JOIN fn_ai_knowledge_index_change AS existing ON existing.DocumentId=document.Id WHERE knowledge.IsEnabled=1 AND document.IsDeleted=0 AND operation.StateKey='completed' AND (existing.Id IS NULL OR existing.VersionNumber<version.VersionNumber) AND (@HasCursor=0 OR document.Id>@AfterDocumentId) AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer INNER JOIN fn_ai_knowledge_import AS ready ON ready.VersionId=newer.Id AND ready.DocumentId=newer.DocumentId WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber AND ready.StateKey='completed') ORDER BY document.Id";
    internal static readonly SqlStatement ScanSqlServer = new("ai.knowledge_incremental_recovery.scan.sql_server", "SELECT TOP (@BatchSize) " + Columns + Scan, SqlDataScope.Global);
    internal static readonly SqlStatement ScanMySql = new("ai.knowledge_incremental_recovery.scan.my_sql", "SELECT " + Columns + Scan + " LIMIT @BatchSize", SqlDataScope.Global);
}
