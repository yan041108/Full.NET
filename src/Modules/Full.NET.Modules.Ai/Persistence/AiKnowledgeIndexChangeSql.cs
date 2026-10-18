using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>所有者目录只读取当前有效的来源锚点；分页不把意图认定为执行队列。</summary>
internal static class AiKnowledgeIndexChangeSql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId=@TenantId";
    private const string Owner = "knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND knowledge.IsEnabled=1 AND ";
    private const string Columns = "document.Id AS DocumentId, document.Title, version.Id AS VersionId, version.VersionNumber, intent.CreatedAtUtc, intent.UpdatedAtUtc";
    private const string From = " FROM fn_ai_knowledge_base AS knowledge INNER JOIN fn_ai_knowledge_document AS document ON document.KnowledgeBaseId=knowledge.Id INNER JOIN fn_ai_knowledge_index_change AS intent ON intent.DocumentId=document.Id INNER JOIN fn_ai_knowledge_version AS version ON version.Id=intent.VersionId AND version.DocumentId=document.Id AND version.VersionNumber=intent.VersionNumber INNER JOIN fn_ai_knowledge_import AS operation ON operation.VersionId=version.Id AND operation.DocumentId=document.Id WHERE " + Owner
        + "knowledge.Version=@KnowledgeBaseVersion AND document.IsDeleted=0 AND operation.StateKey='completed' AND (@HasCursor=0 OR document.Id>@AfterDocumentId) AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer INNER JOIN fn_ai_knowledge_import AS ready ON ready.VersionId=newer.Id AND ready.DocumentId=newer.DocumentId WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber AND ready.StateKey='completed') AND ";
    internal static readonly SqlStatement OwnerHost = new("ai.knowledge_index_change.owner_host", "SELECT knowledge.Version FROM fn_ai_knowledge_base AS knowledge WHERE " + Owner + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement OwnerTenant = new("ai.knowledge_index_change.owner_tenant", "SELECT knowledge.Version FROM fn_ai_knowledge_base AS knowledge WHERE " + Owner + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement SqlServerHost = new("ai.knowledge_index_change.page_sqlserver_host", "SELECT TOP (@Take) " + Columns + From + Host + " ORDER BY document.Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement SqlServerTenant = new("ai.knowledge_index_change.page_sqlserver_tenant", "SELECT TOP (@Take) " + Columns + From + Tenant + " ORDER BY document.Id", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MySqlHost = new("ai.knowledge_index_change.page_mysql_host", "SELECT " + Columns + From + Host + " ORDER BY document.Id LIMIT @Take", SqlDataScope.HostOnly);
    internal static readonly SqlStatement MySqlTenant = new("ai.knowledge_index_change.page_mysql_tenant", "SELECT " + Columns + From + Tenant + " ORDER BY document.Id LIMIT @Take", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement PageFor(DatabaseProvider provider, bool scoped) => provider switch
    {
        DatabaseProvider.SqlServer => scoped ? SqlServerTenant : SqlServerHost,
        DatabaseProvider.MySql => scoped ? MySqlTenant : MySqlHost,
        _ => throw new InvalidOperationException("Unsupported knowledge directory provider.")
    };
}
