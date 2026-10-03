namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识库目录与文档处理审批的独立精确权限。</summary>
public static class AiKnowledgePermissions
{
    /// <summary>读取本人及获授权的启用知识库。</summary>
    public const string Read = "ai.knowledge_bases.read";
    /// <summary>创建私有知识库。</summary>
    public const string Create = "ai.knowledge_bases.create";
    /// <summary>更新本人目录信息。</summary>
    public const string Update = "ai.knowledge_bases.update";
    /// <summary>批准或撤销知识库的精确模型处理范围。</summary>
    public const string PolicyUpdate = "ai.knowledge_bases.policy_update";
    /// <summary>所有者读取明确成员。</summary>
    public const string MembersRead = "ai.knowledge_bases.members_read";
    /// <summary>所有者授予或撤销明确成员。</summary>
    public const string MembersUpdate = "ai.knowledge_bases.members_update";
    /// <summary>所有者读取当前可信租户的活动机构候选，Host 返回空集合。</summary>
    public const string OrganizationCandidatesRead = "ai.knowledge_bases.organization_candidates_read";
    /// <summary>所有者读取当前作用域活动普通角色候选。</summary>
    public const string RoleCandidatesRead = "ai.knowledge_bases.role_candidates_read";
    /// <summary>所有者读取知识库角色授予。</summary>
    public const string RolesRead = "ai.knowledge_bases.roles_read";
    /// <summary>所有者替换知识库角色授予。</summary>
    public const string RolesUpdate = "ai.knowledge_bases.roles_update";
    /// <summary>所有者读取资源的明确机构授予。</summary>
    public const string OrganizationsRead = "ai.knowledge_bases.organizations_read";
    /// <summary>所有者替换资源的明确机构授予，保留用户与角色授予。</summary>
    public const string OrganizationsUpdate = "ai.knowledge_bases.organizations_update";
}
