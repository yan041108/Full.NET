using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>仅清理被完整后继代次替换的已完成载荷；所有权和活动指针在每笔短事务内重验。</summary>
internal static class AiKnowledgeRetiredGenerationSql
{
    internal const string Eligible = " FROM fn_ai_knowledge_index_task AS task INNER JOIN fn_ai_knowledge_document AS document ON document.Id=task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_active_generation AS active ON active.DocumentId=document.Id INNER JOIN fn_ai_knowledge_index_task AS successor ON successor.Id=active.IndexTaskId AND successor.DocumentId=document.Id AND successor.VersionId=active.VersionId WHERE document.IsDeleted=0 AND knowledge.IsEnabled=1 AND task.StateKey='completed' AND successor.StateKey='completed' AND task.Id<>active.IndexTaskId AND successor.DocumentVersion=document.Version AND successor.KnowledgeBaseVersion=knowledge.Version AND successor.ModelConfigId=knowledge.EmbeddingModelConfigId AND successor.ModelConfigVersion=knowledge.EmbeddingModelVersion AND active.ActivatedAtUtc<=@RetiredBefore";
    private const string Ownership = " AND task.Id=@TaskId AND document.Id=@DocumentId AND document.KnowledgeBaseId=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId";
    private const string Host = " AND knowledge.TenantId IS NULL";
    private const string Tenant = " AND knowledge.TenantId=@TenantId";
    private const string Vectors = "DELETE FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT batch.Id FROM fn_ai_knowledge_index_batch AS batch WHERE batch.IndexTaskId=@TaskId)";
    private const string Manifest = "DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId";
    private static string Lock(bool mysql, bool tenant) => "SELECT task.Id" + (mysql ? Eligible : Eligible
        .Replace(" AS task INNER", " AS task WITH(UPDLOCK,HOLDLOCK) INNER")
        .Replace(" AS document ON", " AS document WITH(UPDLOCK,HOLDLOCK) ON")
        .Replace(" AS knowledge ON", " AS knowledge WITH(UPDLOCK,HOLDLOCK) ON")
        .Replace(" AS active ON", " AS active WITH(UPDLOCK,HOLDLOCK) ON")
        .Replace(" AS successor ON", " AS successor WITH(UPDLOCK,HOLDLOCK) ON")) + Ownership + (tenant ? Tenant : Host) + (mysql ? " FOR UPDATE" : string.Empty);
    private static string Delete(string sql, bool tenant) => sql + " AND EXISTS (SELECT 1" + Eligible + Ownership + (tenant ? Tenant : Host) + ")";

    internal static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_retired_generation.lock_sqlserver_host", Lock(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_retired_generation.lock_sqlserver_tenant", Lock(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockMySqlHost = new("ai.knowledge_retired_generation.lock_mysql_host", Lock(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_retired_generation.lock_mysql_tenant", Lock(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement VectorsHost = new("ai.knowledge_retired_generation.vectors_host", Delete(Vectors, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement VectorsTenant = new("ai.knowledge_retired_generation.vectors_tenant", Delete(Vectors, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ManifestHost = new("ai.knowledge_retired_generation.manifest_host", Delete(Manifest, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestTenant = new("ai.knowledge_retired_generation.manifest_tenant", Delete(Manifest, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CurrentVersionHost = new("ai.knowledge_retired_generation.current_version_host", "SELECT active.VersionId" + Eligible + Ownership + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CurrentVersionTenant = new("ai.knowledge_retired_generation.current_version_tenant", "SELECT active.VersionId" + Eligible + Ownership + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement LockFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? LockMySqlTenant : LockMySqlHost : tenant ? LockSqlServerTenant : LockSqlServerHost;
}
