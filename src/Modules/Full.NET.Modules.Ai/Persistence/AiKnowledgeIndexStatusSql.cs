using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>只读 AI 自有表；精确版本、两层权限和删除者墓碑可见性由同一查询约束。</summary>
internal static class AiKnowledgeIndexStatusSql
{
    internal const string Current = "task.DocumentVersion=document.Version AND task.KnowledgeBaseVersion=knowledge.Version AND task.ModelConfigId=knowledge.EmbeddingModelConfigId AND task.ModelConfigVersion=knowledge.EmbeddingModelVersion AND model.Version=task.ModelConfigVersion AND model.IsEnabled=1 AND (model.TenantId IS NULL OR model.TenantId=knowledge.TenantId) AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber) AND ((version.FaqRevisionId IS NULL AND EXISTS (SELECT 1 FROM fn_ai_knowledge_import AS imported WHERE imported.VersionId=version.Id AND imported.DocumentId=document.Id AND imported.FileId=version.FileId AND imported.StateKey='completed' AND imported.ContentHash=version.ContentHash AND imported.ContentType=version.ContentType AND imported.ParsingPolicyKey=version.ParsingPolicyKey AND RIGHT(imported.ParsingPolicyKey,1)<>' ' AND RIGHT(version.ParsingPolicyKey,1)<>' ' AND imported.SizeBytes=version.SizeBytes)) OR (" + AiKnowledgeFaqIndexSql.Origin + ")) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_index_session AS original WHERE original.IndexTaskId=task.Id AND original.ActorUserId=knowledge.OwnerUserId AND (original.TenantId=knowledge.TenantId OR (original.TenantId IS NULL AND knowledge.TenantId IS NULL)))";
    // 分类与生成批准列供 C# 侧处理策略复核；SQL 不在此处过滤枚举值，避免双库排序规则差异。
    internal const string Columns = "NULL AS PublicationId,knowledge.Id AS KnowledgeBaseId,document.Id AS DocumentId,version.Id AS VersionId,knowledge.OwnerUserId,document.Version AS DocumentVersion,knowledge.Version AS KnowledgeBaseVersion," + AiKnowledgeFaqIndexSql.Classification + " AS DataClassification,knowledge.GenerationModelConfigId,knowledge.GenerationModelVersion,CASE WHEN document.IsDeleted=1 THEN 1 ELSE 0 END AS IsDeleted,CASE WHEN EXISTS (SELECT 1 FROM fn_ai_knowledge_import AS cleanup WHERE cleanup.DocumentId=document.Id AND cleanup.StateKey='cleanup_pending') THEN 1 ELSE 0 END AS CleanupPending,CASE WHEN active.DocumentId IS NOT NULL OR EXISTS (SELECT 1 FROM fn_ai_knowledge_text_manifest AS manifest INNER JOIN fn_ai_knowledge_index_task AS indexed ON indexed.Id=manifest.Id WHERE indexed.DocumentId=document.Id) OR EXISTS (SELECT 1 FROM fn_ai_knowledge_document_manifest AS docx INNER JOIN fn_ai_knowledge_index_task AS indexed ON indexed.Id=docx.Id WHERE indexed.DocumentId=document.Id) OR EXISTS (SELECT 1 FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id INNER JOIN fn_ai_knowledge_index_task AS indexed ON indexed.Id=batch.IndexTaskId WHERE indexed.DocumentId=document.Id) THEN 1 ELSE 0 END AS HasDerivedData,task.Id AS TaskId,task.CorrectionRevisionId,task.StateKey,CASE WHEN " + Current + " THEN 1 ELSE 0 END AS IsCurrent,active.IndexTaskId AS ActiveTaskId,active.LeaseToken AS ActiveLeaseToken,active.ReceiptHash AS ActiveReceiptHash,active.ManifestHash AS ActiveManifestHash,active.PlanHash AS ActivePlanHash,version.FileId,version.FaqRevisionId,version.ContentHash,version.ContentType,version.ParsingPolicyKey,version.SizeBytes,model.Id AS ModelConfigId,model.Version AS ModelConfigVersion,model.ProviderKey,model.ModelId";
    private const string Root = " FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id LEFT JOIN fn_ai_model_config AS model ON model.Id=knowledge.EmbeddingModelConfigId LEFT JOIN fn_ai_knowledge_active_generation AS active ON active.DocumentId=document.Id LEFT JOIN fn_ai_knowledge_index_task AS task ON task.Id=(SELECT ";
    private const string Latest = "candidate.Id FROM fn_ai_knowledge_index_task AS candidate WHERE candidate.DocumentId=document.Id AND candidate.VersionId=version.Id ORDER BY candidate.DocumentVersion DESC,candidate.KnowledgeBaseVersion DESC,candidate.Generation DESC,candidate.CreatedAtUtc DESC,candidate.Id DESC";
    private const string Filter = ") WHERE knowledge.Id=@KnowledgeBaseId AND document.Id=@DocumentId AND version.Id=@VersionId AND knowledge.IsEnabled=1 AND (document.IsDeleted=0 OR knowledge.OwnerUserId=@OwnerUserId) AND ";
    // 所有差异在静态初始化时确定；主键查询与已有任务文档/版本索引限制候选范围。
    private static string Read(bool mysql, bool tenant) => "SELECT " + Columns + Root + (mysql ? Latest + " LIMIT 1" : "TOP (1) " + Latest)
        + Filter + (tenant ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL") + " AND " + (mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers);
    internal static readonly SqlStatement ReadSqlServerHost = new("ai.knowledge_index_status.read_sqlserver_host", Read(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadSqlServerTenant = new("ai.knowledge_index_status.read_sqlserver_tenant", Read(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReadMySqlHost = new("ai.knowledge_index_status.read_mysql_host", Read(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadMySqlTenant = new("ai.knowledge_index_status.read_mysql_tenant", Read(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static string ReadFaq(bool mysql, bool scoped) => Read(mysql, scoped).Replace(mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers,
        AiKnowledgeFaqReadSql.Audience(mysql, scoped) + " AND " + AiKnowledgeFaqIndexSql.CurrentOrigin, StringComparison.Ordinal);
    private static readonly SqlStatement FaqSqlServerHost = new("ai.knowledge_index_status.faq_sqlserver_host", ReadFaq(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqSqlServerTenant = new("ai.knowledge_index_status.faq_sqlserver_tenant", ReadFaq(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FaqMySqlHost = new("ai.knowledge_index_status.faq_mysql_host", ReadFaq(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqMySqlTenant = new("ai.knowledge_index_status.faq_mysql_tenant", ReadFaq(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement ForFaq(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? FaqMySqlTenant : FaqMySqlHost : scoped ? FaqSqlServerTenant : FaqSqlServerHost;
    internal static SqlStatement For(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? ReadMySqlTenant : ReadMySqlHost : tenant ? ReadSqlServerTenant : ReadSqlServerHost;

    // 一笔查询复核完整依赖，避免逐来源查询的等待窗口及角色/机构关系重复读取。
    private static readonly string ActiveRoot = Root[..Root.IndexOf(" LEFT JOIN fn_ai_knowledge_index_task AS task", StringComparison.Ordinal)]
        + " LEFT JOIN fn_ai_knowledge_index_task AS task ON task.Id=active.IndexTaskId";
    private static string ActiveRead(bool mysql, bool tenant) => "SELECT " + Columns + ActiveRoot + Filter[1..]
        + (tenant ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL") + " AND " + (mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers);
    private static readonly SqlStatement ActiveSqlServerHost = new("ai.knowledge_index_status.active_sqlserver_host", ActiveRead(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement ActiveSqlServerTenant = new("ai.knowledge_index_status.active_sqlserver_tenant", ActiveRead(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ActiveMySqlHost = new("ai.knowledge_index_status.active_mysql_host", ActiveRead(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement ActiveMySqlTenant = new("ai.knowledge_index_status.active_mysql_tenant", ActiveRead(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement ForActive(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? ActiveMySqlTenant : ActiveMySqlHost : tenant ? ActiveSqlServerTenant : ActiveSqlServerHost;

    private static string History(bool mysql, bool tenant) => AiKnowledgePublishedSourceSql.History(mysql, tenant);
    private static readonly SqlStatement HistorySqlServerHost = new("ai.knowledge_answer.sources_sqlserver_host", History(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement HistorySqlServerTenant = new("ai.knowledge_answer.sources_sqlserver_tenant", History(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement HistoryMySqlHost = new("ai.knowledge_answer.sources_mysql_host", History(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement HistoryMySqlTenant = new("ai.knowledge_answer.sources_mysql_tenant", History(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement HistoryFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? HistoryMySqlTenant : HistoryMySqlHost : tenant ? HistorySqlServerTenant : HistorySqlServerHost;
}
