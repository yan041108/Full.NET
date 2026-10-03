using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>文件版本只允许插入；所有读取同时核对范围、启用状态及双层明确成员授权。</summary>
internal static class AiKnowledgeVersionSql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId = @TenantId";
    private const string Access = "(knowledge.OwnerUserId = @OwnerUserId OR (EXISTS (SELECT 1 FROM fn_ai_knowledge_member AS baseMember WHERE baseMember.KnowledgeBaseId = knowledge.Id AND baseMember.UserId = @OwnerUserId) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_document_member AS docMember WHERE docMember.DocumentId = document.Id AND docMember.UserId = @OwnerUserId)))";
    private const string Columns = "version.Id, version.DocumentId, version.FileId, version.VersionNumber, version.OriginalFileName, version.ContentType, version.ContentHash, version.SizeBytes, version.UploadedByUserId, version.CreatedAtUtc";
    private const string Joins = " FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id = version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND " + Access + " AND ";
    private const string Read = "SELECT " + Columns + Joins;
    private const string Insert = "INSERT INTO fn_ai_knowledge_version (Id, DocumentId, FileId, VersionNumber, OriginalFileName, ContentType, ContentHash, SizeBytes, UploadedByUserId, CreatedAtUtc) SELECT operation.VersionId, operation.DocumentId, operation.FileId, @VersionNumber, operation.OriginalFileName, operation.ContentType, operation.ContentHash, operation.SizeBytes, operation.ActorUserId, @Now FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id = operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND operation.Id = @OperationId AND operation.LeaseToken = @LeaseToken AND operation.LeaseExpiresAtUtc > @Now AND operation.StateKey = 'file_uploaded' AND operation.FileId IS NOT NULL AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND knowledge.OwnerUserId = @OwnerUserId AND ";

    internal static readonly SqlStatement ReadHost = new("ai.knowledge_version.read_host", Read + Host + " AND version.Id = @VersionId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadTenant = new("ai.knowledge_version.read_tenant", Read + Tenant + " AND version.Id = @VersionId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountHost = new("ai.knowledge_version.count_host", "SELECT COUNT(*)" + Joins + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountTenant = new("ai.knowledge_version.count_tenant", "SELECT COUNT(*)" + Joins + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServerHost = new("ai.knowledge_version.list_sqlserver_host", Read + Host + " ORDER BY version.VersionNumber DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("ai.knowledge_version.list_sqlserver_tenant", Read + Tenant + " ORDER BY version.VersionNumber DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("ai.knowledge_version.list_mysql_host", Read + Host + " ORDER BY version.VersionNumber DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("ai.knowledge_version.list_mysql_tenant", Read + Tenant + " ORDER BY version.VersionNumber DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_version.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_version.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

}
