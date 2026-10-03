using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>仅 JOIN 本模块知识库表；所有成员读写通过可信范围及所有者核对。</summary>
internal static class AiKnowledgeMemberSql
{
    private const string HostOwner = "knowledge.TenantId IS NULL AND knowledge.OwnerUserId = @OwnerUserId";
    private const string TenantOwner = "knowledge.TenantId = @TenantId AND knowledge.OwnerUserId = @OwnerUserId";
    private const string SelectMembers = "SELECT member.UserId FROM fn_ai_knowledge_member AS member INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = member.KnowledgeBaseId WHERE knowledge.Id = @Id AND ";
    private const string DeleteMembers = "DELETE FROM fn_ai_knowledge_member WHERE KnowledgeBaseId = @Id AND EXISTS (SELECT 1 FROM fn_ai_knowledge_base AS knowledge WHERE knowledge.Id = @Id AND ";
    private const string InsertMember = "INSERT INTO fn_ai_knowledge_member (Id, KnowledgeBaseId, UserId, CreatedAtUtc) SELECT @MemberId, knowledge.Id, @UserId, @CreatedAtUtc FROM fn_ai_knowledge_base AS knowledge WHERE knowledge.Id = @Id AND ";
    private const string BumpVersion = "UPDATE fn_ai_knowledge_base SET Version = Version + 1, UpdatedAtUtc = @UpdatedAtUtc WHERE Id = @Id AND Version = @Version AND Version < 2147483647 AND OwnerUserId = @OwnerUserId AND ";

    internal static readonly SqlStatement ListHost = new("ai.knowledge.list_members_host", SelectMembers + HostOwner, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListTenant = new("ai.knowledge.list_members_tenant", SelectMembers + TenantOwner, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteHost = new("ai.knowledge.delete_members_host", DeleteMembers + HostOwner + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteTenant = new("ai.knowledge.delete_members_tenant", DeleteMembers + TenantOwner + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge.insert_member_host", InsertMember + HostOwner, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge.insert_member_tenant", InsertMember + TenantOwner, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement BumpHost = new("ai.knowledge.bump_members_host", BumpVersion + "TenantId IS NULL", SqlDataScope.HostOnly);
    internal static readonly SqlStatement BumpTenant = new("ai.knowledge.bump_members_tenant", BumpVersion + "TenantId = @TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
