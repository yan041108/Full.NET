using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>元数据通知仅查一个范围内的最新确认来源；不以消息顺序代替当前文档状态。</summary>
internal static class AiKnowledgeMetadataSourceSql
{
    private const string Columns = "knowledge.Id AS KnowledgeBaseId, document.Id AS DocumentId, version.Id AS VersionId, knowledge.TenantId";
    private const string Source = " FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId INNER JOIN fn_ai_knowledge_version AS version ON version.DocumentId=document.Id INNER JOIN fn_ai_knowledge_import AS operation ON operation.VersionId=version.Id AND operation.DocumentId=document.Id WHERE document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND document.Version>=@MetadataVersion AND document.IsDeleted=0 AND knowledge.IsEnabled=1 AND operation.StateKey='completed' AND ";
    private const string Host = "knowledge.TenantId IS NULL ORDER BY version.VersionNumber DESC";
    private const string Tenant = "knowledge.TenantId=@TenantId ORDER BY version.VersionNumber DESC";
    internal static readonly SqlStatement SqlServerHost = new("ai.knowledge_metadata_source.sql_server_host", "SELECT TOP (1) " + Columns + Source + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement SqlServerTenant = new("ai.knowledge_metadata_source.sql_server_tenant", "SELECT TOP (1) " + Columns + Source + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MySqlHost = new("ai.knowledge_metadata_source.my_sql_host", "SELECT " + Columns + Source + Host + " LIMIT 1", SqlDataScope.HostOnly);
    internal static readonly SqlStatement MySqlTenant = new("ai.knowledge_metadata_source.my_sql_tenant", "SELECT " + Columns + Source + Tenant + " LIMIT 1", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement For(DatabaseProvider provider, bool tenant) => provider switch
    {
        DatabaseProvider.SqlServer => tenant ? SqlServerTenant : SqlServerHost,
        DatabaseProvider.MySql => tenant ? MySqlTenant : MySqlHost,
        _ => throw new InvalidOperationException("Unsupported knowledge metadata provider.")
    };
}
