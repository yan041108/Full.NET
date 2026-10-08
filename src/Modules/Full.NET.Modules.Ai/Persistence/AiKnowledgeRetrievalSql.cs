using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>只读 AI 表，双层权限在 SQL 内预过滤；上限加一使超限可被明确拒绝。</summary>
internal static class AiKnowledgeRetrievalSql
{
    private const string Current = "task.DocumentVersion=document.Version AND task.KnowledgeBaseVersion=knowledge.Version AND task.ModelConfigId=knowledge.EmbeddingModelConfigId AND task.ModelConfigVersion=knowledge.EmbeddingModelVersion AND model.Version=task.ModelConfigVersion AND model.IsEnabled=1 AND (model.TenantId IS NULL OR model.TenantId=knowledge.TenantId) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_import AS imported WHERE imported.VersionId=version.Id AND imported.DocumentId=document.Id AND imported.FileId=version.FileId AND imported.StateKey='completed' AND imported.ContentHash=version.ContentHash AND imported.ContentType=version.ContentType AND imported.SizeBytes=version.SizeBytes) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_index_session AS original WHERE original.IndexTaskId=task.Id AND original.ActorUserId=knowledge.OwnerUserId AND (original.TenantId=knowledge.TenantId OR (original.TenantId IS NULL AND knowledge.TenantId IS NULL)))";
    private const string Columns = "knowledge.Id AS KnowledgeBaseId,document.Id AS DocumentId,version.Id AS VersionId,knowledge.OwnerUserId,document.Version AS DocumentVersion,knowledge.Version AS KnowledgeBaseVersion,0 AS IsDeleted,CASE WHEN EXISTS (SELECT 1 FROM fn_ai_knowledge_import AS cleanup WHERE cleanup.DocumentId=document.Id AND cleanup.StateKey='cleanup_pending') THEN 1 ELSE 0 END AS CleanupPending,CASE WHEN active.DocumentId IS NOT NULL THEN 1 ELSE 0 END AS HasDerivedData,task.Id AS TaskId,task.StateKey,CASE WHEN " + Current + " THEN 1 ELSE 0 END AS IsCurrent,active.IndexTaskId AS ActiveTaskId,active.LeaseToken AS ActiveLeaseToken,active.ReceiptHash AS ActiveReceiptHash,active.ManifestHash AS ActiveManifestHash,active.PlanHash AS ActivePlanHash,version.FileId,version.ContentHash,version.ContentType,version.SizeBytes,model.Id AS ModelConfigId,model.Version AS ModelConfigVersion,model.ProviderKey,model.ModelId";
    private const string Root = " FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id LEFT JOIN fn_ai_model_config AS model ON model.Id=knowledge.EmbeddingModelConfigId LEFT JOIN fn_ai_knowledge_active_generation AS active ON active.DocumentId=document.Id AND active.VersionId=version.Id LEFT JOIN fn_ai_knowledge_index_task AS task ON task.Id=active.IndexTaskId";
    private const string Live = "knowledge.IsEnabled=1 AND document.IsDeleted=0 AND knowledge.EmbeddingModelConfigId=@ModelConfigId AND knowledge.EmbeddingModelVersion=@ModelConfigVersion AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber)";
    private static string Ids(bool mysql, string alias, string parameter, string column) => mysql
        ? $"EXISTS (SELECT 1 FROM JSON_TABLE(@{parameter}, '$[*]' COLUMNS (Id CHAR(36) PATH '$')) AS {alias} WHERE UUID_TO_BIN({alias}.Id,0)={column})"
        : $"EXISTS (SELECT 1 FROM OPENJSON(@{parameter}) WITH (Id uniqueidentifier '$') AS {alias} WHERE {alias}.Id={column})";
    private static string Filter(bool mysql, bool scoped) => (scoped ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL")
        + " AND " + (mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers) + " AND " + Live
        + " AND model.IsEnabled=1 AND model.Version=@ModelConfigVersion AND ((model.TenantId IS NULL AND @ExpectedModelTenantId IS NULL) OR model.TenantId=@ExpectedModelTenantId)"
        + " AND " + Exact(mysql, "model.EndpointBaseUrl", "ExpectedEndpointBaseUrl")
        + " AND " + Exact(mysql, "model.ProviderKey", "ExpectedProviderKey") + " AND " + Exact(mysql, "model.ModelId", "ExpectedModelId")
        + " AND " + Exact(mysql, "model.OrganizationId", "ExpectedOrganizationId") + " AND " + Exact(mysql, "model.ApiKeyProtected", "ExpectedApiKeyProtected")
        + " AND " + Generation(mysql, scoped);
    // 可选生成模型和来源在同一权威查询判定；不以版本号替代目的地、凭据和租户范围。
    private static string Generation(bool mysql, bool scoped) => "(@GenerationModelConfigId IS NULL OR EXISTS (SELECT 1 FROM fn_ai_model_config AS generation WHERE generation.Id=@GenerationModelConfigId AND generation.IsEnabled=1 AND generation.Version=@GenerationModelConfigVersion AND "
        + (scoped ? "(generation.TenantId IS NULL OR generation.TenantId=@TenantId)" : "generation.TenantId IS NULL")
        + " AND ((generation.TenantId IS NULL AND @GenerationTenantId IS NULL) OR generation.TenantId=@GenerationTenantId)"
        + " AND " + Exact(mysql, "generation.EndpointBaseUrl", "GenerationEndpointBaseUrl")
        + " AND " + Exact(mysql, "generation.ProviderKey", "GenerationProviderKey") + " AND " + Exact(mysql, "generation.ModelId", "GenerationModelId")
        + " AND " + Exact(mysql, "generation.OrganizationId", "GenerationOrganizationId") + " AND " + Exact(mysql, "generation.ApiKeyProtected", "GenerationApiKeyProtected") + "))";
    // 字符串绑定必须保留大小写、尾空格和空值语义，不能继承提供程序默认排序规则。
    private static string Exact(bool mysql, string column, string parameter) => $"(({column} IS NULL AND @{parameter} IS NULL) OR "
        + (mysql ? $"CAST({column} AS BINARY)=CAST(@{parameter} AS BINARY)" : $"CONVERT(varbinary(max),CONVERT(nvarchar(max),{column}))=CONVERT(varbinary(max),CONVERT(nvarchar(max),@{parameter}))") + ")";
    private static string SourceText(bool mysql, bool scoped) =>
        "SELECT " + (mysql ? "" : "TOP (9) ") + Columns + Root + " WHERE " + Filter(mysql, scoped) + " AND "
        + Ids(mysql, "requested", "KnowledgeBaseIdsJson", "knowledge.Id") + " ORDER BY document.Id" + (mysql ? " LIMIT 9" : "");
    private static string VectorText(bool mysql, bool scoped) =>
        "SELECT " + (mysql ? "" : "TOP (33) ") + "vector.Id,batch.IndexTaskId,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,task.ModelConfigId,task.ModelConfigVersion,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey,vector.PayloadHash,SUBSTRING(vector.Payload,1,524289) AS Payload,vector.CreatedAtUtc"
        + Root + " INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.IndexTaskId=task.Id INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id WHERE "
        + Filter(mysql, scoped) + " AND " + Ids(mysql, "requested", "TaskIdsJson", "task.Id") + " ORDER BY document.Id,batch.BatchOrdinal" + (mysql ? " LIMIT 33" : "");
    // DOCX 召回需要完整逻辑清单；只读当前授权来源，截断上限加一使超限明确失败。
    private static string DocxManifestText(bool mysql, bool scoped) =>
        "SELECT " + (mysql ? "" : "TOP (1) ") + "manifest.Id,manifest.FormatKey,manifest.PayloadHash,SUBSTRING(manifest.Payload,1,8388609) AS Payload,manifest.CreatedAtUtc"
        + Root + " INNER JOIN fn_ai_knowledge_docx_manifest AS manifest ON manifest.Id=task.Id WHERE " + Filter(mysql, scoped)
        + " AND document.Id=@DocumentId AND version.Id=@VersionId AND task.Id=@TaskId AND version.ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document'"
        + (mysql ? " LIMIT 1" : "");
    private static readonly SqlStatement DocxManifestSqlServerHost = new("ai.knowledge_retrieval.docx_manifest_sqlserver_host", DocxManifestText(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement DocxManifestSqlServerTenant = new("ai.knowledge_retrieval.docx_manifest_sqlserver_tenant", DocxManifestText(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement DocxManifestMySqlHost = new("ai.knowledge_retrieval.docx_manifest_mysql_host", DocxManifestText(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement DocxManifestMySqlTenant = new("ai.knowledge_retrieval.docx_manifest_mysql_tenant", DocxManifestText(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement DocxManifestFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? DocxManifestMySqlTenant : DocxManifestMySqlHost : scoped ? DocxManifestSqlServerTenant : DocxManifestSqlServerHost;
    private static readonly SqlStatement SourcesSqlServerHost = new("ai.knowledge_retrieval.sources_sqlserver_host", SourceText(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement SourcesSqlServerTenant = new("ai.knowledge_retrieval.sources_sqlserver_tenant", SourceText(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SourcesMySqlHost = new("ai.knowledge_retrieval.sources_mysql_host", SourceText(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement SourcesMySqlTenant = new("ai.knowledge_retrieval.sources_mysql_tenant", SourceText(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement VectorsSqlServerHost = new("ai.knowledge_retrieval.vectors_sqlserver_host", VectorText(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement VectorsSqlServerTenant = new("ai.knowledge_retrieval.vectors_sqlserver_tenant", VectorText(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement VectorsMySqlHost = new("ai.knowledge_retrieval.vectors_mysql_host", VectorText(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement VectorsMySqlTenant = new("ai.knowledge_retrieval.vectors_mysql_tenant", VectorText(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement SourcesFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? SourcesMySqlTenant : SourcesMySqlHost : scoped ? SourcesSqlServerTenant : SourcesSqlServerHost;
    internal static SqlStatement VectorsFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? VectorsMySqlTenant : VectorsMySqlHost : scoped ? VectorsSqlServerTenant : VectorsSqlServerHost;
}
