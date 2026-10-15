using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>增量意图只连接 AI 自有表；单文档锁与单调版本序号保证自然幂等。</summary>
internal static class AiKnowledgeIncrementalIndexSql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId=@TenantId";
    private const string Scope = "document.Id=@DocumentId AND document.KnowledgeBaseId=@KnowledgeBaseId AND ";
    private const string Authority = "SELECT version.VersionNumber FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id INNER JOIN fn_ai_knowledge_import AS operation ON operation.VersionId=version.Id AND operation.DocumentId=document.Id WHERE " + Scope + "document.IsDeleted=0 AND knowledge.IsEnabled=1 AND version.Id=@VersionId AND operation.StateKey='completed' AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer INNER JOIN fn_ai_knowledge_import AS ready ON ready.VersionId=newer.Id AND ready.DocumentId=newer.DocumentId WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber AND ready.StateKey='completed') AND ";
    private const string Owned = "EXISTS (SELECT 1 FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE " + Scope;
    private const string Update = "UPDATE fn_ai_knowledge_index_change SET VersionId=@VersionId, VersionNumber=@VersionNumber, UpdatedAtUtc=@Now WHERE DocumentId=@DocumentId AND VersionNumber<@VersionNumber AND " + Owned;
    private const string Delete = "DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId AND " + Owned;
    // MySQL 禁止在 INSERT 的子查询中读取目标表；文档锁内改用主查询 LEFT JOIN 判断首次创建。
    private const string Insert = "INSERT INTO fn_ai_knowledge_index_change (Id,DocumentId,VersionId,VersionNumber,CreatedAtUtc,UpdatedAtUtc) SELECT @Id,document.Id,@VersionId,@VersionNumber,@Now,@Now FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId LEFT JOIN fn_ai_knowledge_index_change AS existing ON existing.DocumentId=document.Id WHERE existing.Id IS NULL AND document.IsDeleted=0 AND knowledge.IsEnabled=1 AND " + Scope;
    private static string Locked(string sql) => sql.Replace(" AS document ", " AS document WITH(UPDLOCK,HOLDLOCK) ", StringComparison.Ordinal);
    internal static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_index_change.lock_sqlserver_host", Locked(Authority + Host), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_index_change.lock_sqlserver_tenant", Locked(Authority + Tenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockMySqlHost = new("ai.knowledge_index_change.lock_mysql_host", Authority + Host + " FOR UPDATE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_index_change.lock_mysql_tenant", Authority + Tenant + " FOR UPDATE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateHost = new("ai.knowledge_index_change.update_host", Update + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement UpdateTenant = new("ai.knowledge_index_change.update_tenant", Update + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_index_change.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_index_change.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteHost = new("ai.knowledge_index_change.delete_host", Delete + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteTenant = new("ai.knowledge_index_change.delete_tenant", Delete + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement LockFor(DatabaseProvider provider, bool tenant) => provider switch
    {
        DatabaseProvider.SqlServer => tenant ? LockSqlServerTenant : LockSqlServerHost,
        DatabaseProvider.MySql => tenant ? LockMySqlTenant : LockMySqlHost,
        _ => throw new InvalidOperationException("Unsupported knowledge database provider."),
    };
}
