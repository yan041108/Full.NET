using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>文件版本只允许插入；所有读取同时核对范围、启用状态及双层明确成员授权。</summary>
internal static class AiKnowledgeVersionSql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId = @TenantId";
    private const string Access = AiKnowledgeReadSql.Layers;
    private const string MySqlAccess = AiKnowledgeReadSql.LayersMySql;
    private const string Columns = "version.Id, version.DocumentId, version.FileId, version.VersionNumber, version.OriginalFileName, version.ContentType,version.ParsingPolicyKey, version.ContentHash, version.SizeBytes, version.UploadedByUserId, version.CreatedAtUtc";
    // 可信租户谓词先于包含 JSON 行集的授权子查询，保持执行器可静态确认的范围形状。
    private const string Joins = " FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id = version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND ";
    // 意图不是执行授权；已创建任务也不等于索引已 ready。按精确来源快照判断是否仍需当前会话确认。
    private const string Processing = ", CASE WHEN EXISTS (SELECT 1 FROM fn_ai_knowledge_index_task AS task WHERE task.VersionId=version.Id AND task.DocumentId=document.Id AND task.DocumentVersion=document.Version AND task.KnowledgeBaseVersion=knowledge.Version) THEN 'index_admitted' WHEN EXISTS (SELECT 1 FROM fn_ai_knowledge_index_change AS intent WHERE intent.DocumentId=document.Id AND intent.VersionId=version.Id) THEN 'awaiting_index_confirmation' ELSE 'pending_processing' END AS ProcessingStatus";
    private const string Read = "SELECT " + Columns + Processing + Joins;
    private const string Insert = "INSERT INTO fn_ai_knowledge_version (Id, DocumentId, FileId, VersionNumber, OriginalFileName, ContentType, ParsingPolicyKey, ContentHash, SizeBytes, UploadedByUserId, CreatedAtUtc) SELECT operation.VersionId, operation.DocumentId, operation.FileId, @VersionNumber, operation.OriginalFileName, operation.ContentType,operation.ParsingPolicyKey, operation.ContentHash, operation.SizeBytes, operation.ActorUserId, @Now FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id = operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND operation.Id = @OperationId AND operation.LeaseToken = @LeaseToken AND operation.LeaseExpiresAtUtc > @Now AND operation.StateKey = 'file_uploaded' AND operation.FileId IS NOT NULL AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND knowledge.OwnerUserId = @OwnerUserId AND ";

    internal static readonly SqlStatement ReadHost = new("ai.knowledge_version.read_host", Read + Host + " AND " + Access + " AND version.Id = @VersionId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadMySqlHost = new("ai.knowledge_version.read_mysql_host", Read + Host + " AND " + MySqlAccess + " AND version.Id = @VersionId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadTenant = new("ai.knowledge_version.read_tenant", Read + Tenant + " AND " + Access + " AND version.Id = @VersionId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReadMySqlTenant = new("ai.knowledge_version.read_mysql_tenant", Read + Tenant + " AND " + MySqlAccess + " AND version.Id = @VersionId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountHost = new("ai.knowledge_version.count_host", "SELECT COUNT(*)" + Joins + Host + " AND " + Access, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountMySqlHost = new("ai.knowledge_version.count_mysql_host", "SELECT COUNT(*)" + Joins + Host + " AND " + MySqlAccess, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountTenant = new("ai.knowledge_version.count_tenant", "SELECT COUNT(*)" + Joins + Tenant + " AND " + Access, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountMySqlTenant = new("ai.knowledge_version.count_mysql_tenant", "SELECT COUNT(*)" + Joins + Tenant + " AND " + MySqlAccess, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string OwnedCount = "SELECT COUNT(*) FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id = version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND knowledge.OwnerUserId = @OwnerUserId AND ";
    // 上传提交计算版本号仅查询 AI 所有者状态，不在写事务内解析角色关系。
    internal static readonly SqlStatement CountOwnedHost = new("ai.knowledge_version.count_owned_host", OwnedCount + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountOwnedTenant = new("ai.knowledge_version.count_owned_tenant", OwnedCount + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServerHost = new("ai.knowledge_version.list_sqlserver_host", Read + Host + " AND " + Access + " ORDER BY version.VersionNumber DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("ai.knowledge_version.list_sqlserver_tenant", Read + Tenant + " AND " + Access + " ORDER BY version.VersionNumber DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("ai.knowledge_version.list_mysql_host", Read + Host + " AND " + MySqlAccess + " ORDER BY version.VersionNumber DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("ai.knowledge_version.list_mysql_tenant", Read + Tenant + " AND " + MySqlAccess + " ORDER BY version.VersionNumber DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_version.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_version.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static SqlStatement ReadFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? ReadMySqlTenant : ReadMySqlHost : tenant ? ReadTenant : ReadHost;
    internal static SqlStatement CountFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? CountMySqlTenant : CountMySqlHost : tenant ? CountTenant : CountHost;
}
