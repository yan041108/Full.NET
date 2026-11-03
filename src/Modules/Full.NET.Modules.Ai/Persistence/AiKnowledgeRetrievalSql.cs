using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>只读 AI 表，双层权限在 SQL 内预过滤；上限加一使超限可被明确拒绝。</summary>
internal static partial class AiKnowledgeRetrievalSql
{
    // 分部文件中的静态语句也会读取投影，不能依赖不同文件的字段初始化顺序。
    private static string Columns => AiKnowledgePublishedSourceSql.Columns;
    private const string Root = AiKnowledgePublishedSourceSql.Root;
    private const string Live = "knowledge.IsEnabled=1 AND document.IsDeleted=0 AND knowledge.EmbeddingModelConfigId=@ModelConfigId AND knowledge.EmbeddingModelVersion=@ModelConfigVersion";
    private static string Ids(bool mysql, string alias, string parameter, string column) => mysql
        ? $"EXISTS (SELECT 1 FROM JSON_TABLE(@{parameter}, '$[*]' COLUMNS (Id CHAR(36) PATH '$')) AS {alias} WHERE UUID_TO_BIN({alias}.Id,0)={column})"
        : $"EXISTS (SELECT 1 FROM OPENJSON(@{parameter}) WITH (Id uniqueidentifier '$') AS {alias} WHERE {alias}.Id={column})";
    private static string Filter(bool mysql, bool scoped) => (scoped ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL")
        + " AND " + (mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers) + " AND " + Live + " AND " + AiKnowledgePublishedSourceSql.Live(mysql)
        + " AND model.IsEnabled=1 AND model.Version=@ModelConfigVersion AND ((model.TenantId IS NULL AND @ExpectedModelTenantId IS NULL) OR model.TenantId=@ExpectedModelTenantId)"
        + " AND " + Exact(mysql, "model.EndpointBaseUrl", "ExpectedEndpointBaseUrl")
        + " AND " + Exact(mysql, "model.ProviderKey", "ExpectedProviderKey") + " AND " + Exact(mysql, "model.ModelId", "ExpectedModelId")
        + " AND " + Exact(mysql, "model.OrganizationId", "ExpectedOrganizationId") + " AND " + Exact(mysql, "model.ApiKeyProtected", "ExpectedApiKeyProtected")
        + " AND " + Classification(mysql) + " AND " + Generation(mysql, scoped);
    // 分类使用二进制精确比较，禁止数据库排序规则放行大小写或尾空格变体。
    private static string Classification(bool mysql) => mysql
        ? "CAST(knowledge.DataClassification AS BINARY) IN (CAST('public' AS BINARY),CAST('internal' AS BINARY),CAST('restricted' AS BINARY))"
        : "CONVERT(varbinary(max),CONVERT(nvarchar(max),knowledge.DataClassification)) IN (CONVERT(varbinary(max),N'public'),CONVERT(varbinary(max),N'internal'),CONVERT(varbinary(max),N'restricted'))";
    // 可选生成模型和来源在同一权威查询判定；不以版本号替代目的地、凭据和租户范围。
    private static string Generation(bool mysql, bool scoped) => "(@GenerationModelConfigId IS NULL OR (knowledge.GenerationModelConfigId=@GenerationModelConfigId AND knowledge.GenerationModelVersion=@GenerationModelConfigVersion AND EXISTS (SELECT 1 FROM fn_ai_model_config AS generation WHERE generation.Id=@GenerationModelConfigId AND generation.IsEnabled=1 AND generation.Version=@GenerationModelConfigVersion AND "
        + (scoped ? "(generation.TenantId IS NULL OR generation.TenantId=@TenantId)" : "generation.TenantId IS NULL")
        + " AND ((generation.TenantId IS NULL AND @GenerationTenantId IS NULL) OR generation.TenantId=@GenerationTenantId)"
        + " AND " + Exact(mysql, "generation.EndpointBaseUrl", "GenerationEndpointBaseUrl")
        + " AND " + Exact(mysql, "generation.ProviderKey", "GenerationProviderKey") + " AND " + Exact(mysql, "generation.ModelId", "GenerationModelId")
        + " AND " + Exact(mysql, "generation.OrganizationId", "GenerationOrganizationId") + " AND " + Exact(mysql, "generation.ApiKeyProtected", "GenerationApiKeyProtected") + ")))";
    // 字符串绑定必须保留大小写、尾空格和空值语义，不能继承提供程序默认排序规则。
    private static string Exact(bool mysql, string column, string parameter) => $"(({column} IS NULL AND @{parameter} IS NULL) OR "
        + (mysql ? $"CAST({column} AS BINARY)=CAST(@{parameter} AS BINARY)" : $"CONVERT(varbinary(max),CONVERT(nvarchar(max),{column}))=CONVERT(varbinary(max),CONVERT(nvarchar(max),@{parameter}))") + ")";
    private static string SourceText(bool mysql, bool scoped, bool search = false) =>
        "SELECT " + (mysql ? "" : "TOP (9) ") + Columns + Root + " WHERE " + Filter(mysql, scoped) + " AND "
        + Ids(mysql, "requested", "KnowledgeBaseIdsJson", "knowledge.Id")
        + (search ? " AND (@AllDocuments=1 OR " + Ids(mysql, "selected", "DocumentIdsJson", "document.Id") + ")" : "")
        + " ORDER BY document.Id" + (mysql ? " LIMIT 9" : "");
    // 标题批量读取与冻结来源使用相同发布/权限条件，复用既有静态文档物化器。
    private static string SearchDocumentText(bool mysql, bool scoped) => "SELECT " + (mysql ? "" : "TOP (9) ")
        + "document.Id,document.KnowledgeBaseId,document.Title,document.Description,document.CreatedAtUtc,document.UpdatedAtUtc,document.Version"
        + Root + " WHERE " + Filter(mysql, scoped) + " AND " + Ids(mysql, "requested", "TaskIdsJson", "task.Id")
        + " ORDER BY document.Id" + (mysql ? " LIMIT 9" : "");
    private static readonly SqlStatement SearchSourcesSqlServerHost = new("ai.knowledge_search.sources_sqlserver_host", SourceText(false, false, true), SqlDataScope.HostOnly);
    private static readonly SqlStatement SearchSourcesSqlServerTenant = new("ai.knowledge_search.sources_sqlserver_tenant", SourceText(false, true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SearchSourcesMySqlHost = new("ai.knowledge_search.sources_mysql_host", SourceText(true, false, true), SqlDataScope.HostOnly);
    private static readonly SqlStatement SearchSourcesMySqlTenant = new("ai.knowledge_search.sources_mysql_tenant", SourceText(true, true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SearchDocumentsSqlServerHost = new("ai.knowledge_search.documents_sqlserver_host", SearchDocumentText(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement SearchDocumentsSqlServerTenant = new("ai.knowledge_search.documents_sqlserver_tenant", SearchDocumentText(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SearchDocumentsMySqlHost = new("ai.knowledge_search.documents_mysql_host", SearchDocumentText(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement SearchDocumentsMySqlTenant = new("ai.knowledge_search.documents_mysql_tenant", SearchDocumentText(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement SearchSourcesFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? SearchSourcesMySqlTenant : SearchSourcesMySqlHost : scoped ? SearchSourcesSqlServerTenant : SearchSourcesSqlServerHost;
    internal static SqlStatement SearchDocumentsFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? SearchDocumentsMySqlTenant : SearchDocumentsMySqlHost : scoped ? SearchDocumentsSqlServerTenant : SearchDocumentsSqlServerHost;
    private static string VectorText(bool mysql, bool scoped) =>
        "SELECT " + (mysql ? "" : "TOP (33) ") + "vector.Id,batch.IndexTaskId,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,task.ModelConfigId,task.ModelConfigVersion,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey,vector.PayloadHash,SUBSTRING(vector.Payload,1,524289) AS Payload,vector.CreatedAtUtc"
        + Root + " INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.IndexTaskId=task.Id INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id WHERE "
        + Filter(mysql, scoped) + " AND " + Ids(mysql, "requested", "TaskIdsJson", "task.Id") + " ORDER BY document.Id,batch.BatchOrdinal" + (mysql ? " LIMIT 33" : "");
    // DOCX 召回需要完整逻辑清单；只读当前授权来源，截断上限加一使超限明确失败。
    private static string DocumentManifestText(bool mysql, bool scoped) =>
        "SELECT " + (mysql ? "" : "TOP (1) ") + "manifest.Id,manifest.FormatKey,manifest.PayloadHash,SUBSTRING(manifest.Payload,1,16777216) AS Payload,manifest.CreatedAtUtc"
        + Root + " INNER JOIN fn_ai_knowledge_document_manifest AS manifest ON manifest.Id=task.Id WHERE " + Filter(mysql, scoped)
        + " AND document.Id=@DocumentId AND version.Id=@VersionId AND task.Id=@TaskId"
        + (mysql ? " LIMIT 1" : "");
    private static readonly SqlStatement DocumentManifestSqlServerHost = new("ai.knowledge_retrieval.document_manifest_sqlserver_host", DocumentManifestText(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement DocumentManifestSqlServerTenant = new("ai.knowledge_retrieval.document_manifest_sqlserver_tenant", DocumentManifestText(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement DocumentManifestMySqlHost = new("ai.knowledge_retrieval.document_manifest_mysql_host", DocumentManifestText(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement DocumentManifestMySqlTenant = new("ai.knowledge_retrieval.document_manifest_mysql_tenant", DocumentManifestText(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement DocumentManifestFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? DocumentManifestMySqlTenant : DocumentManifestMySqlHost : scoped ? DocumentManifestSqlServerTenant : DocumentManifestSqlServerHost;
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
