using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>发布引用、明确证据保留期和读取租约共同阻止清理；墓碑只等待在途资产排空。</summary>
internal static class AiKnowledgePublicationCleanupSql
{
    internal const string Retained = " AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_read_lease AS held WHERE held.GenerationId=task.Id AND held.ExpiresAtUtc>@Now) AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_publication AS publication LEFT JOIN fn_ai_knowledge_publication_head AS head ON head.PublicationId=publication.Id WHERE publication.GenerationId=task.Id AND (publication.UpdatedAtUtc>@RetiredBefore OR (publication.StateKey='published' AND publication.EffectiveToUtc>@RetiredBefore)))";
    private const string Owner = " FROM fn_ai_knowledge_index_task AS task INNER JOIN fn_ai_knowledge_document AS document ON document.Id=task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE task.Id=@TaskId AND document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId";
    private const string Host = " AND knowledge.TenantId IS NULL";
    private const string Tenant = " AND knowledge.TenantId=@TenantId";
    // 旧版本没有保留完整回执的退休任务只写不可读清理标记，绝不补造可发布的证明。
    private const string Marker = "INSERT INTO fn_ai_knowledge_generation (Id,DocumentId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc,CleanupState) SELECT task.Id,task.DocumentId,task.VersionId,'','','','',task.Id,task.CreatedAtUtc,1" + Owner + " AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_generation WHERE Id=task.Id)";
    private const string Mark = "UPDATE fn_ai_knowledge_generation SET CleanupState=1 WHERE Id=@TaskId AND CleanupState=0 AND EXISTS (SELECT 1" + Owner;
    private const string Finish = "UPDATE fn_ai_knowledge_generation SET CleanupState=2,ReceiptJson='' WHERE Id=@TaskId AND CleanupState=1 AND EXISTS (SELECT 1" + Owner;
    internal static readonly SqlStatement MarkerHost = new("ai.knowledge_publication_cleanup.marker_host", Marker + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkerTenant = new("ai.knowledge_publication_cleanup.marker_tenant", Marker + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MarkHost = new("ai.knowledge_publication_cleanup.mark_host", Mark + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkTenant = new("ai.knowledge_publication_cleanup.mark_tenant", Mark + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FinishHost = new("ai.knowledge_publication_cleanup.finish_host", Finish + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FinishTenant = new("ai.knowledge_publication_cleanup.finish_tenant", Finish + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
