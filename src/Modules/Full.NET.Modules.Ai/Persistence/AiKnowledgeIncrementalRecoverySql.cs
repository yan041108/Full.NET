using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>恢复只分页读取本人启用知识库内的最新确认来源；没有跨模块读取或全租户扫描。</summary>
internal static class AiKnowledgeIncrementalRecoverySql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId=@TenantId";
    private const string Owner = "knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND knowledge.IsEnabled=1 AND ";
    private const string Columns = "knowledge.Id AS KnowledgeBaseId, document.Id AS DocumentId, version.Id AS VersionId, knowledge.TenantId";
    private const string From = " FROM fn_ai_knowledge_base AS knowledge INNER JOIN fn_ai_knowledge_document AS document ON document.KnowledgeBaseId=knowledge.Id INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id INNER JOIN fn_ai_knowledge_import AS operation ON operation.VersionId=version.Id AND operation.DocumentId=document.Id WHERE " + Owner
        + "document.IsDeleted=0 AND operation.StateKey='completed' AND (@HasCursor=0 OR document.Id>@AfterDocumentId) AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer INNER JOIN fn_ai_knowledge_import AS ready ON ready.VersionId=newer.Id AND ready.DocumentId=newer.DocumentId WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber AND ready.StateKey='completed') AND ";
    internal static readonly SqlStatement OwnerHost = new("ai.knowledge_recovery.owner_host", "SELECT 1 FROM fn_ai_knowledge_base AS knowledge WHERE " + Owner + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement OwnerTenant = new("ai.knowledge_recovery.owner_tenant", "SELECT 1 FROM fn_ai_knowledge_base AS knowledge WHERE " + Owner + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement SqlServerHost = new("ai.knowledge_recovery.page_sqlserver_host", "SELECT TOP (@Take) " + Columns + From + Host + " ORDER BY document.Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement SqlServerTenant = new("ai.knowledge_recovery.page_sqlserver_tenant", "SELECT TOP (@Take) " + Columns + From + Tenant + " ORDER BY document.Id", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MySqlHost = new("ai.knowledge_recovery.page_mysql_host", "SELECT " + Columns + From + Host + " ORDER BY document.Id LIMIT @Take", SqlDataScope.HostOnly);
    internal static readonly SqlStatement MySqlTenant = new("ai.knowledge_recovery.page_mysql_tenant", "SELECT " + Columns + From + Tenant + " ORDER BY document.Id LIMIT @Take", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement PageFor(DatabaseProvider provider, bool scoped) => provider switch
    {
        DatabaseProvider.SqlServer => scoped ? SqlServerTenant : SqlServerHost,
        DatabaseProvider.MySql => scoped ? MySqlTenant : MySqlHost,
        _ => throw new InvalidOperationException("Unsupported knowledge database provider.")
    };
}
