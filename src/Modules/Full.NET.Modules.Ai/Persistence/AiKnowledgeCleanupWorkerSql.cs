using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>只扫描 AI 墓碑下的可清理项；游标及排序均由同一数据库比较 UUID，避免两库排序差异。</summary>
internal static class AiKnowledgeCleanupWorkerSql
{
    private const string Scope = "knowledge.TenantId, knowledge.Id AS KnowledgeBaseId, document.Id AS DocumentId, knowledge.OwnerUserId";
    private const string Joins = " INNER JOIN fn_ai_knowledge_document AS document ON document.Id=work.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE document.IsDeleted=1";
    private const string Tasks = "SELECT work.Id, 0 AS Kind, work.CreatedAtUtc, " + Scope + " FROM fn_ai_knowledge_index_task AS work" + Joins
        + " AND (work.StateKey IN ('queued','running') OR EXISTS (SELECT 1 FROM fn_ai_knowledge_active_generation AS active WHERE active.IndexTaskId=work.Id AND active.DocumentId=document.Id) OR EXISTS (SELECT 1 FROM fn_ai_knowledge_text_manifest AS manifest WHERE manifest.Id=work.Id) OR EXISTS (SELECT 1 FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id WHERE batch.IndexTaskId=work.Id))";
    private const string Imports = "SELECT work.Id, 1 AS Kind, work.CreatedAtUtc, " + Scope + " FROM fn_ai_knowledge_import AS work" + Joins
        + " AND work.StateKey='cleanup_pending' AND (work.LeaseToken IS NULL OR work.LeaseExpiresAtUtc<=@Now)";
    private const string Scan = " FROM (" + Tasks + " UNION ALL " + Imports + ") AS candidate WHERE (@HasCursor=0 OR candidate.CreatedAtUtc>@AfterCreatedAtUtc OR (candidate.CreatedAtUtc=@AfterCreatedAtUtc AND (candidate.Kind>@AfterKind OR (candidate.Kind=@AfterKind AND candidate.Id>@AfterId)))) ORDER BY candidate.CreatedAtUtc,candidate.Kind,candidate.Id";
    private const string Columns = "candidate.Id,candidate.Kind,candidate.CreatedAtUtc,candidate.TenantId,candidate.KnowledgeBaseId,candidate.DocumentId,candidate.OwnerUserId";
    internal static readonly SqlStatement ScanSqlServer = new("ai.knowledge_cleanup_worker.scan.sqlserver", "SELECT TOP (@BatchSize) " + Columns + Scan, SqlDataScope.Global);
    internal static readonly SqlStatement ScanMySql = new("ai.knowledge_cleanup_worker.scan.mysql", "SELECT " + Columns + Scan + " LIMIT @BatchSize", SqlDataScope.Global);
}
