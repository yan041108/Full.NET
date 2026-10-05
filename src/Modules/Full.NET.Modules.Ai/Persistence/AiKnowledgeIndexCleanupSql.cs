using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>仅删除墓碑所属的派生载荷；历史任务、会话、批次和费用凭据不删除。</summary>
internal static class AiKnowledgeIndexCleanupSql
{
    private const string Owned = " FROM fn_ai_knowledge_index_task AS task INNER JOIN fn_ai_knowledge_document AS document ON document.Id=task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE document.Id=@DocumentId AND document.KnowledgeBaseId=@KnowledgeBaseId AND document.IsDeleted=1 AND knowledge.OwnerUserId=@OwnerUserId AND ";
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId=@TenantId";
    private const string Pending = " AND (task.StateKey IN ('queued','running') OR EXISTS (SELECT 1 FROM fn_ai_knowledge_active_generation AS active WHERE active.IndexTaskId=task.Id AND active.DocumentId=document.Id) OR EXISTS (SELECT 1 FROM fn_ai_knowledge_text_manifest AS manifest WHERE manifest.Id=task.Id) OR EXISTS (SELECT 1 FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id WHERE batch.IndexTaskId=task.Id)) ORDER BY task.CreatedAtUtc,task.Id";
    private const string Guard = " AND EXISTS (SELECT 1" + Owned;
    // 先持有与写清单、向量和激活相同的任务锁；不靠租约超时推断旧执行者已经退出。
    private static string Lock(bool mysql, bool tenant) => "SELECT task.Id" + (mysql ? Owned : Owned.Replace(" AS task INNER", " AS task WITH(UPDLOCK,HOLDLOCK) INNER"))
        + (tenant ? Tenant : Host) + " AND task.Id=@TaskId" + (mysql ? " FOR UPDATE" : string.Empty);
    private static string List(bool mysql, bool tenant) => (mysql ? "SELECT task.Id" : "SELECT TOP (25) task.Id") + Owned
        + (tenant ? Tenant : Host) + Pending + (mysql ? " LIMIT 25" : string.Empty);
    private const string Cancel = "UPDATE fn_ai_knowledge_index_task SET StateKey='cancelled',LeaseToken=NULL,LeaseExpiresAtUtc=NULL,UpdatedAtUtc=@Now WHERE Id=@TaskId AND StateKey IN ('queued','running')" + AiKnowledgeIndexTaskSql.OwnerExists;
    private const string Pointer = "DELETE FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId AND IndexTaskId=@TaskId";
    private const string Vectors = "DELETE FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT batch.Id FROM fn_ai_knowledge_index_batch AS batch WHERE batch.IndexTaskId=@TaskId)";
    private const string Manifest = "DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId";
    private static string Delete(string sql, bool tenant) => sql + Guard + (tenant ? Tenant : Host) + " AND task.Id=@TaskId)";

    internal static readonly SqlStatement ListSqlServerHost = new("ai.knowledge_index_cleanup.list_sqlserver_host", List(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("ai.knowledge_index_cleanup.list_sqlserver_tenant", List(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("ai.knowledge_index_cleanup.list_mysql_host", List(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("ai.knowledge_index_cleanup.list_mysql_tenant", List(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_index_cleanup.lock_sqlserver_host", Lock(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_index_cleanup.lock_sqlserver_tenant", Lock(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockMySqlHost = new("ai.knowledge_index_cleanup.lock_mysql_host", Lock(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_index_cleanup.lock_mysql_tenant", Lock(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CancelHost = new("ai.knowledge_index_cleanup.cancel_host", Cancel + Host + " AND document.IsDeleted=1)", SqlDataScope.HostOnly);
    internal static readonly SqlStatement CancelTenant = new("ai.knowledge_index_cleanup.cancel_tenant", Cancel + Tenant + " AND document.IsDeleted=1)", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PointerHost = new("ai.knowledge_index_cleanup.pointer_host", Delete(Pointer, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement PointerTenant = new("ai.knowledge_index_cleanup.pointer_tenant", Delete(Pointer, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement VectorsHost = new("ai.knowledge_index_cleanup.vectors_host", Delete(Vectors, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement VectorsTenant = new("ai.knowledge_index_cleanup.vectors_tenant", Delete(Vectors, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ManifestHost = new("ai.knowledge_index_cleanup.manifest_host", Delete(Manifest, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestTenant = new("ai.knowledge_index_cleanup.manifest_tenant", Delete(Manifest, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement ListFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? ListMySqlTenant : ListMySqlHost : tenant ? ListSqlServerTenant : ListSqlServerHost;
    internal static SqlStatement LockFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? LockMySqlTenant : LockMySqlHost : tenant ? LockSqlServerTenant : LockSqlServerHost;
}
