using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>机构标识是权威目录引用；SQL 仅连接 AI 自有父资源，不能连接 Organization。</summary>
internal static class AiKnowledgeOrganizationGrantSql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId = @TenantId";
    private const string Owner = "knowledge.OwnerUserId = @OwnerUserId AND ";
    private const string BaseList = "SELECT grantObject.OrganizationUnitId FROM fn_ai_knowledge_org AS grantObject INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = grantObject.KnowledgeBaseId WHERE knowledge.Id = @KnowledgeBaseId AND " + Owner;
    private const string BaseDelete = "DELETE FROM fn_ai_knowledge_org WHERE KnowledgeBaseId = @KnowledgeBaseId AND EXISTS (SELECT 1 FROM fn_ai_knowledge_base AS knowledge WHERE knowledge.Id = @KnowledgeBaseId AND " + Owner;
    private const string BaseInsert = "INSERT INTO fn_ai_knowledge_org (Id, KnowledgeBaseId, OrganizationUnitId, CreatedAtUtc) SELECT @GrantId, knowledge.Id, @OrganizationUnitId, @CreatedAtUtc FROM fn_ai_knowledge_base AS knowledge WHERE knowledge.Id = @KnowledgeBaseId AND " + Owner;
    private const string DocParent = "SELECT 1 FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND " + Owner;
    private const string DocList = "SELECT grantObject.OrganizationUnitId FROM fn_ai_knowledge_document_org AS grantObject INNER JOIN fn_ai_knowledge_document AS document ON document.Id = grantObject.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND " + Owner;
    private const string DocDelete = "DELETE FROM fn_ai_knowledge_document_org WHERE DocumentId = @DocumentId AND EXISTS (" + DocParent;
    private const string DocInsert = "INSERT INTO fn_ai_knowledge_document_org (Id, DocumentId, OrganizationUnitId, CreatedAtUtc) SELECT @GrantId, @DocumentId, @OrganizationUnitId, @CreatedAtUtc WHERE EXISTS (" + DocParent;
    internal static readonly SqlStatement ListBaseHost = new("ai.knowledge_org.list_base_host", BaseList + Host + " ORDER BY grantObject.OrganizationUnitId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListBaseTenant = new("ai.knowledge_org.list_base_tenant", BaseList + Tenant + " ORDER BY grantObject.OrganizationUnitId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteBaseHost = new("ai.knowledge_org.delete_base_host", BaseDelete + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteBaseTenant = new("ai.knowledge_org.delete_base_tenant", BaseDelete + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertBaseHost = new("ai.knowledge_org.insert_base_host", BaseInsert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertBaseTenant = new("ai.knowledge_org.insert_base_tenant", BaseInsert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListDocHost = new("ai.knowledge_org.list_doc_host", DocList + Host + " ORDER BY grantObject.OrganizationUnitId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListDocTenant = new("ai.knowledge_org.list_doc_tenant", DocList + Tenant + " ORDER BY grantObject.OrganizationUnitId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteDocHost = new("ai.knowledge_org.delete_doc_host", DocDelete + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteDocTenant = new("ai.knowledge_org.delete_doc_tenant", DocDelete + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertDocHost = new("ai.knowledge_org.insert_doc_host", DocInsert + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertDocTenant = new("ai.knowledge_org.insert_doc_tenant", DocInsert + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    // 软删除在墓碑事务内清除机构授予，不能依赖文件清理完成后再拒读。
    private const string Cleanup = "DELETE FROM fn_ai_knowledge_document_org WHERE DocumentId = @DocumentId AND EXISTS (SELECT 1 FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND " + Owner;
    internal static readonly SqlStatement CleanupDocHost = new("ai.knowledge_org.cleanup_doc_host", Cleanup + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement CleanupDocTenant = new("ai.knowledge_org.cleanup_doc_tenant", Cleanup + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
