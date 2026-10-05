using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>后台候选不是派发授权；领取仍在可信租户作用域内竞争当前来源。</summary>
internal static class AiKnowledgeIndexWorkerSql
{
    private const string Columns = "fn_ai_knowledge_index_task.Id AS TaskId, knowledge.TenantId, knowledge.Id AS KnowledgeBaseId, fn_ai_knowledge_index_task.DocumentId, fn_ai_knowledge_index_task.VersionId, fn_ai_knowledge_index_task.DocumentVersion, fn_ai_knowledge_index_task.KnowledgeBaseVersion, knowledge.OwnerUserId";
    // 全局入口只引导 AI 自有任务到独立作用域；原会话和当前来源必须匹配，不能补造执行身份。
    private const string Candidates = " FROM fn_ai_knowledge_index_task INNER JOIN fn_ai_knowledge_document AS document ON document.Id = fn_ai_knowledge_index_task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.Id = fn_ai_knowledge_index_task.VersionId AND version.DocumentId = document.Id INNER JOIN fn_ai_knowledge_index_session AS binding ON binding.IndexTaskId = fn_ai_knowledge_index_task.Id WHERE binding.ActorUserId = knowledge.OwnerUserId AND (binding.TenantId = knowledge.TenantId OR (binding.TenantId IS NULL AND knowledge.TenantId IS NULL)) AND fn_ai_knowledge_index_task.AttemptCount < 3 AND (fn_ai_knowledge_index_task.StateKey = 'queued' OR (fn_ai_knowledge_index_task.StateKey = 'running' AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc <= @Now)) AND version.ContentType IN ('text/plain', 'text/markdown')" + AiKnowledgeIndexTaskSql.CurrentAuthority + " ORDER BY fn_ai_knowledge_index_task.CreatedAtUtc, fn_ai_knowledge_index_task.Id";
    internal static readonly SqlStatement ScanSqlServer = new("ai.knowledge_index_worker.scan.sqlserver", "SELECT TOP (@BatchSize) " + Columns + Candidates, SqlDataScope.Global);
    internal static readonly SqlStatement ScanMySql = new("ai.knowledge_index_worker.scan.mysql", "SELECT " + Columns + Candidates + " LIMIT @BatchSize", SqlDataScope.Global);
}
