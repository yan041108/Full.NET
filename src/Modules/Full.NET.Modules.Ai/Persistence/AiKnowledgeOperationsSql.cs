using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>只读 AI 自有维护元数据；所有筛选与游标均在相同可信范围和所有者约束内执行。</summary>
internal static class AiKnowledgeOperationsSql
{
    private static string First(bool mysql, string sql) => mysql ? sql + " LIMIT 1" : "TOP (1) " + sql;
    private static string Page(bool mysql, bool tenant)
    {
        var scope = tenant ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL";
        var faqScope = tenant ? "faq.TenantId=@TenantId" : "faq.TenantId IS NULL";
        return "SELECT " + (mysql ? "" : "TOP (26) ") + """
            knowledge.Id AS KnowledgeBaseId, document.Id AS DocumentId, version.Id AS VersionId, version.VersionNumber,
            faq.Id AS FaqId, CASE WHEN version.FaqRevisionId IS NULL THEN 'document-v1' ELSE 'faq-revision-v1' END AS SourceKind,
            CASE WHEN version.FaqRevisionId IS NOT NULL THEN 'static_revision' ELSE imported.StateKey END AS PreparationState,
            task.Id AS TaskId, task.StateKey AS TaskState, COALESCE(task.AttemptCount,0) AS AttemptCount,
            version.CreatedAtUtc, task.UpdatedAtUtc AS TaskUpdatedAtUtc, task.LeaseExpiresAtUtc AS TaskLeaseExpiresAtUtc,
            CASE WHEN active.IndexTaskId IS NOT NULL AND active.VersionId=version.Id THEN 1 ELSE 0 END AS HasActiveGeneration,
            reviewed.StateKey AS ReviewState, publication.Id AS PublicationId, publication.VersionId AS PublicationVersionId,
            publication.StateKey AS PublicationState, publication.EffectiveFromUtc, publication.EffectiveToUtc
            FROM fn_ai_knowledge_version AS version
            INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId
            INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId
            LEFT JOIN fn_ai_knowledge_faq AS faq ON faq.ReservedDocumentId=document.Id AND faq.KnowledgeBaseId=knowledge.Id
            LEFT JOIN fn_ai_knowledge_faq_revision AS revision ON revision.Id=version.FaqRevisionId AND revision.FaqId=faq.Id
            LEFT JOIN fn_ai_knowledge_active_generation AS active ON active.DocumentId=document.Id
            LEFT JOIN fn_ai_knowledge_publication_head AS head ON head.Id=document.Id
            LEFT JOIN fn_ai_knowledge_publication AS publication ON publication.Id=head.PublicationId AND publication.DocumentId=document.Id
            """ + " LEFT JOIN fn_ai_knowledge_import AS imported ON imported.Id=(SELECT "
            + First(mysql, "candidate.Id FROM fn_ai_knowledge_import AS candidate WHERE candidate.VersionId=version.Id AND candidate.DocumentId=document.Id ORDER BY candidate.CreatedAtUtc DESC,candidate.Id DESC") + ")"
            + " LEFT JOIN fn_ai_knowledge_index_task AS task ON task.Id=(SELECT "
            + First(mysql, "candidate.Id FROM fn_ai_knowledge_index_task AS candidate WHERE candidate.VersionId=version.Id AND candidate.DocumentId=document.Id ORDER BY candidate.Generation DESC,candidate.CreatedAtUtc DESC,candidate.Id DESC") + ")"
            + " LEFT JOIN fn_ai_knowledge_publication AS reviewed ON reviewed.Id=(SELECT "
            + First(mysql, "candidate.Id FROM fn_ai_knowledge_publication AS candidate WHERE candidate.VersionId=version.Id AND candidate.DocumentId=document.Id ORDER BY candidate.CreatedAtUtc DESC,candidate.Id DESC") + ")"
            + " WHERE " + scope + " AND knowledge.OwnerUserId=@ActorUserId AND knowledge.IsEnabled=1 AND document.IsDeleted=0"
            + " AND (@KnowledgeBaseId IS NULL OR knowledge.Id=@KnowledgeBaseId) AND (@AfterId IS NULL OR version.Id>@AfterId)"
            // 受管 FAQ 即使状态损坏也不能落回普通文件分支；这里仅允许准确静态修订身份。
            + " AND ((version.FaqRevisionId IS NULL AND version.FileId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_faq AS managed WHERE managed.ReservedDocumentId=document.Id))"
            + " OR (version.FaqRevisionId IS NOT NULL AND version.FileId IS NULL AND revision.Id IS NOT NULL AND " + faqScope + "))"
            + " AND ((version.CreatedAtUtc>=@FromUtc AND version.CreatedAtUtc<=@ToUtc)"
            + " OR (task.UpdatedAtUtc>=@FromUtc AND task.UpdatedAtUtc<=@ToUtc)"
            + " OR (imported.UpdatedAtUtc>=@FromUtc AND imported.UpdatedAtUtc<=@ToUtc)"
            + " OR (publication.VersionId=version.Id AND publication.EffectiveToUtc>=@FromUtc AND publication.EffectiveToUtc<=@ToUtc))"
            + " AND (@View='all' OR (@View='failed' AND (task.StateKey='failed' OR imported.StateKey='failed' OR (task.StateKey='running' AND (task.LeaseExpiresAtUtc IS NULL OR task.LeaseExpiresAtUtc<=@Now))))"
            + " OR (@View='expired' AND publication.VersionId=version.Id AND publication.StateKey='published' AND publication.EffectiveToUtc<=@Now))"
            + " ORDER BY version.Id" + (mysql ? " LIMIT 26" : "");
    }
    private static readonly SqlStatement SqlServerHost = new("ai.knowledge_operations.page_sqlserver_host", Page(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement SqlServerTenant = new("ai.knowledge_operations.page_sqlserver_tenant", Page(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement MySqlHost = new("ai.knowledge_operations.page_mysql_host", Page(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement MySqlTenant = new("ai.knowledge_operations.page_mysql_tenant", Page(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement For(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? MySqlTenant : MySqlHost : tenant ? SqlServerTenant : SqlServerHost;
}
