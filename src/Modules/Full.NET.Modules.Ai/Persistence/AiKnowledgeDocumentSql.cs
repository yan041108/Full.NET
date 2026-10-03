using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>标题与授权均只读 AI 自有表；知识库及文档授权每次按权威状态联合核对。</summary>
internal static class AiKnowledgeDocumentSql
{
    private const string Columns = "document.Id, document.KnowledgeBaseId, document.Title, document.Description, document.CreatedAtUtc, document.UpdatedAtUtc, document.Version";
    private const string Join = " FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND ";
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId = @TenantId";
    private const string Owner = "knowledge.OwnerUserId = @OwnerUserId";
    private const string Access = "(knowledge.OwnerUserId = @OwnerUserId OR (EXISTS (SELECT 1 FROM fn_ai_knowledge_member AS baseMember WHERE baseMember.KnowledgeBaseId = knowledge.Id AND baseMember.UserId = @OwnerUserId) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_document_member AS docMember WHERE docMember.DocumentId = document.Id AND docMember.UserId = @OwnerUserId)))";
    private const string Read = "SELECT " + Columns + Join + "document.Id = @DocumentId AND ";
    private const string List = "SELECT " + Columns + Join;
    private const string Count = "SELECT COUNT(*)" + Join;
    private const string Insert = "INSERT INTO fn_ai_knowledge_document (Id, KnowledgeBaseId, Title, Description, IsDeleted, CreatedAtUtc, Version) SELECT @DocumentId, knowledge.Id, @Title, @Description, 0, @CreatedAtUtc, 1 FROM fn_ai_knowledge_base AS knowledge WHERE knowledge.Id = @KnowledgeBaseId AND knowledge.IsEnabled = 1 AND " + Owner + " AND ";
    // 每个写入同时检查文档版本、启用目录和所有者，失败结果由统一事务执行器回滚。
    private const string WriteWhere = " WHERE Id = @DocumentId AND KnowledgeBaseId = @KnowledgeBaseId AND IsDeleted = 0 AND Version = @Version AND Version < 2147483647 AND EXISTS (SELECT 1 FROM fn_ai_knowledge_base AS knowledge WHERE knowledge.Id = @KnowledgeBaseId AND knowledge.IsEnabled = 1 AND " + Owner + " AND ";
    private const string Update = "UPDATE fn_ai_knowledge_document SET Title = @Title, Description = @Description, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1" + WriteWhere;
    private const string Bump = "UPDATE fn_ai_knowledge_document SET UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1" + WriteWhere;
    private const string Delete = "UPDATE fn_ai_knowledge_document SET IsDeleted = 1, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1" + WriteWhere;
    private const string ListMembers = "SELECT member.UserId FROM fn_ai_knowledge_document_member AS member INNER JOIN fn_ai_knowledge_document AS document ON document.Id = member.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND " + Owner + " AND ";
    private const string DeleteMembers = "DELETE FROM fn_ai_knowledge_document_member WHERE DocumentId = @DocumentId AND EXISTS (SELECT 1 FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND " + Owner + " AND ";
    private const string InsertMember = "INSERT INTO fn_ai_knowledge_document_member (Id, DocumentId, UserId, CreatedAtUtc) SELECT @MemberId, document.Id, @UserId, @CreatedAtUtc FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND EXISTS (SELECT 1 FROM fn_ai_knowledge_member AS member WHERE member.KnowledgeBaseId = knowledge.Id AND member.UserId = @UserId) AND " + Owner + " AND ";

    internal static readonly SqlStatement FindHost = new("ai.knowledge_document.find_host", Read + Host + " AND " + Owner, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge_document.find_tenant", Read + Tenant + " AND " + Owner, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReadHost = new("ai.knowledge_document.read_host", Read + Host + " AND " + Access, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadTenant = new("ai.knowledge_document.read_tenant", Read + Tenant + " AND " + Access, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountHost = new("ai.knowledge_document.count_host", Count + Host + " AND " + Access, SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountTenant = new("ai.knowledge_document.count_tenant", Count + Tenant + " AND " + Access, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServerHost = new("ai.knowledge_document.list_sqlserver_host", List + Host + " AND " + Access + " ORDER BY document.CreatedAtUtc DESC, document.Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("ai.knowledge_document.list_sqlserver_tenant", List + Tenant + " AND " + Access + " ORDER BY document.CreatedAtUtc DESC, document.Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("ai.knowledge_document.list_mysql_host", List + Host + " AND " + Access + " ORDER BY document.CreatedAtUtc DESC, document.Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("ai.knowledge_document.list_mysql_tenant", List + Tenant + " AND " + Access + " ORDER BY document.CreatedAtUtc DESC, document.Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_document.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_document.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateHost = new("ai.knowledge_document.update_host", Update + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement UpdateTenant = new("ai.knowledge_document.update_tenant", Update + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement BumpHost = new("ai.knowledge_document.bump_host", Bump + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement BumpTenant = new("ai.knowledge_document.bump_tenant", Bump + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteHost = new("ai.knowledge_document.delete_host", Delete + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteTenant = new("ai.knowledge_document.delete_tenant", Delete + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMembersHost = new("ai.knowledge_document.list_members_host", ListMembers + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMembersTenant = new("ai.knowledge_document.list_members_tenant", ListMembers + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteMembersHost = new("ai.knowledge_document.delete_members_host", DeleteMembers + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteMembersTenant = new("ai.knowledge_document.delete_members_tenant", DeleteMembers + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertMemberHost = new("ai.knowledge_document.insert_member_host", InsertMember + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertMemberTenant = new("ai.knowledge_document.insert_member_tenant", InsertMember + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
