using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>精确主键查询当前所有者及最新来源；不存在、非所有者与失效批准统一拒绝。</summary>
internal static class AiKnowledgeIndexEnqueueSql
{
    // 仅查询 AI 自有表，使用既有主键/文档版本索引；Host 模型可获准供租户使用。
    private const string Read = "SELECT knowledge.Id AS KnowledgeBaseId,document.Id AS DocumentId,version.Id AS VersionId,knowledge.OwnerUserId,document.Version AS DocumentVersion,knowledge.Version AS KnowledgeBaseVersion,model.Id AS ModelConfigId,model.Version AS ModelConfigVersion,version.ContentType FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id INNER JOIN fn_ai_model_config AS model ON model.Id=knowledge.EmbeddingModelConfigId WHERE document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND version.Id=@VersionId AND knowledge.OwnerUserId=@OwnerUserId AND document.IsDeleted=0 AND knowledge.IsEnabled=1 AND model.Version=knowledge.EmbeddingModelVersion AND model.IsEnabled=1 AND (model.TenantId IS NULL OR model.TenantId=knowledge.TenantId) AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_import AS operation WHERE operation.VersionId=version.Id AND operation.DocumentId=document.Id AND operation.FileId=version.FileId AND operation.StateKey='completed') AND ";
    internal static readonly SqlStatement SourceHost = new("ai.knowledge_index_enqueue.source_host", Read + "knowledge.TenantId IS NULL", SqlDataScope.HostOnly);
    internal static readonly SqlStatement SourceTenant = new("ai.knowledge_index_enqueue.source_tenant", Read + "knowledge.TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    // 对账只在导入与不可变来源指纹一致时提供恢复提示，避免将来源损坏误判为可重建索引。
    private const string ReconciliationSource = " AND EXISTS (SELECT 1 FROM fn_ai_knowledge_import AS sourceImport WHERE sourceImport.VersionId=version.Id AND sourceImport.DocumentId=document.Id AND sourceImport.FileId=version.FileId AND sourceImport.StateKey='completed' AND sourceImport.ContentHash=version.ContentHash AND sourceImport.SizeBytes=version.SizeBytes AND sourceImport.ContentType=version.ContentType)";
    internal static readonly SqlStatement ReconciliationSourceHost = new("ai.knowledge_index_reconciliation.source_host", Read + "knowledge.TenantId IS NULL" + ReconciliationSource, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReconciliationSourceTenant = new("ai.knowledge_index_reconciliation.source_tenant", Read + "knowledge.TenantId=@TenantId" + ReconciliationSource, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
