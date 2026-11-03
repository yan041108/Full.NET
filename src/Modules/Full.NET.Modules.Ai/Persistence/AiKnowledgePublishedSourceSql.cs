using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>正式内容从当前有效发布头读取；候选证明与当前权限分别约束，旧授权不进入快照。</summary>
internal static class AiKnowledgePublishedSourceSql
{
    internal static readonly string Current = AiKnowledgeIndexStatusSql.Current
        .Replace("task.DocumentVersion=document.Version AND task.KnowledgeBaseVersion=knowledge.Version AND ", "", StringComparison.Ordinal)
        .Replace(" AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber)", "", StringComparison.Ordinal)
        + " AND active.CleanupState=0";
    internal static readonly string GenerationColumns = AiKnowledgeIndexStatusSql.Columns
        .Replace(AiKnowledgeIndexStatusSql.Current, Current, StringComparison.Ordinal)
        .Replace("document.Version AS DocumentVersion", "task.DocumentVersion AS DocumentVersion", StringComparison.Ordinal)
        .Replace("knowledge.Version AS KnowledgeBaseVersion", "task.KnowledgeBaseVersion AS KnowledgeBaseVersion", StringComparison.Ordinal)
        .Replace("active.IndexTaskId", "active.Id", StringComparison.Ordinal)
        .Replace("cleanup.DocumentId=document.Id AND cleanup.StateKey", "cleanup.DocumentId=document.Id AND cleanup.VersionId=version.Id AND cleanup.StateKey", StringComparison.Ordinal);
    internal static readonly string Columns = GenerationColumns.Replace("NULL AS PublicationId", "publication.Id AS PublicationId", StringComparison.Ordinal);
    internal const string GenerationRoot = " FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id INNER JOIN fn_ai_knowledge_generation AS active ON active.DocumentId=document.Id AND active.VersionId=version.Id INNER JOIN fn_ai_knowledge_index_task AS task ON task.Id=active.Id AND task.DocumentId=document.Id AND task.VersionId=version.Id LEFT JOIN fn_ai_model_config AS model ON model.Id=knowledge.EmbeddingModelConfigId";
    internal const string Root = GenerationRoot + " INNER JOIN fn_ai_knowledge_publication_head AS head ON head.Id=document.Id INNER JOIN fn_ai_knowledge_publication AS publication ON publication.Id=head.PublicationId AND publication.DocumentId=document.Id AND publication.GenerationId=task.Id AND publication.VersionId=version.Id";
    internal static string Live(bool mysql) => "publication.StateKey='published' AND publication.EffectiveFromUtc<=" + Now(mysql)
        + " AND publication.EffectiveToUtc>" + Now(mysql) + " AND active.CleanupState=0";
    private static string Now(bool mysql) => mysql ? "UTC_TIMESTAMP(6)" : "SYSUTCDATETIME()";
    private static string Scope(bool mysql, bool scoped) => (scoped ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL")
        + " AND knowledge.IsEnabled=1 AND document.IsDeleted=0 AND " + (mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers);
    internal static string Readable(bool mysql, bool scoped) => "SELECT " + Columns + Root + " WHERE " + Scope(mysql, scoped) + " AND " + Live(mysql);
    internal static string History(bool mysql, bool scoped) => Readable(mysql, scoped) + " AND EXISTS (SELECT 1 FROM "
        + (mysql ? "JSON_TABLE(@DependenciesJson,'$[*]' COLUMNS (KnowledgeBaseId CHAR(36) PATH '$.knowledgeBaseId',DocumentId CHAR(36) PATH '$.documentId',VersionId CHAR(36) PATH '$.versionId')) AS requested WHERE UUID_TO_BIN(requested.KnowledgeBaseId,0)=knowledge.Id AND UUID_TO_BIN(requested.DocumentId,0)=document.Id AND UUID_TO_BIN(requested.VersionId,0)=version.Id)"
            : "OPENJSON(@DependenciesJson) WITH (KnowledgeBaseId uniqueidentifier '$.knowledgeBaseId',DocumentId uniqueidentifier '$.documentId',VersionId uniqueidentifier '$.versionId') AS requested WHERE requested.KnowledgeBaseId=knowledge.Id AND requested.DocumentId=document.Id AND requested.VersionId=version.Id)");
    private static string Generation(bool mysql, bool scoped) => "SELECT " + GenerationColumns.Replace("NULL AS PublicationId", "@PublicationId AS PublicationId", StringComparison.Ordinal)
        + GenerationRoot + " WHERE " + Scope(mysql, scoped) + " AND knowledge.Id=@KnowledgeBaseId AND document.Id=@DocumentId AND version.Id=@VersionId AND task.Id=@TaskId AND active.CleanupState=0"
        + " AND (@PublicationId IS NULL OR EXISTS (SELECT 1 FROM fn_ai_knowledge_publication_head AS head INNER JOIN fn_ai_knowledge_publication AS publication ON publication.Id=head.PublicationId WHERE head.Id=document.Id AND publication.Id=@PublicationId AND publication.DocumentId=document.Id AND publication.GenerationId=task.Id AND publication.VersionId=version.Id AND " + Live(mysql) + "))";
    private static readonly SqlStatement GenerationSqlServerHost = new("ai.knowledge_publication.generation_sqlserver_host", Generation(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement GenerationSqlServerTenant = new("ai.knowledge_publication.generation_sqlserver_tenant", Generation(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement GenerationMySqlHost = new("ai.knowledge_publication.generation_mysql_host", Generation(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement GenerationMySqlTenant = new("ai.knowledge_publication.generation_mysql_tenant", Generation(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement ForGeneration(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? GenerationMySqlTenant : GenerationMySqlHost : scoped ? GenerationSqlServerTenant : GenerationSqlServerHost;

    // FAQ 专属证明只接收真实数据库修订；普通 ForGeneration 的排除条件不变。
    private static string FaqGeneration(bool mysql, bool scoped) => Generation(mysql, scoped).Replace(mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers,
        AiKnowledgeFaqReadSql.Audience(mysql, scoped) + " AND " + AiKnowledgeFaqIndexSql.Origin, StringComparison.Ordinal);
    private static readonly SqlStatement FaqGenerationSqlServerHost = new("ai.knowledge_publication.faq_generation_sqlserver_host", FaqGeneration(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqGenerationSqlServerTenant = new("ai.knowledge_publication.faq_generation_sqlserver_tenant", FaqGeneration(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FaqGenerationMySqlHost = new("ai.knowledge_publication.faq_generation_mysql_host", FaqGeneration(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqGenerationMySqlTenant = new("ai.knowledge_publication.faq_generation_mysql_tenant", FaqGeneration(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement ForFaqGeneration(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? FaqGenerationMySqlTenant : FaqGenerationMySqlHost : scoped ? FaqGenerationSqlServerTenant : FaqGenerationSqlServerHost;
}
