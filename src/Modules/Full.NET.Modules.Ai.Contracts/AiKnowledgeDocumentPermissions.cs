namespace Full.NET.Modules.Ai.Contracts;

/// <summary>文档管理精确权限；目录读取权不自动授予文档读取。</summary>
public static class AiKnowledgeDocumentPermissions
{
    /// <summary>所有者分页读取可靠确认的最新来源目录，不授予执行或收费资格。</summary>
    public const string IndexChangesRead = "ai.knowledge_documents.index_changes_read";
    /// <summary>所有者分页补齐历史来源与停用期间的增量意图，不授予收费执行权。</summary>
    public const string IndexRecover = "ai.knowledge_documents.index_recover";
    /// <summary>所有者显式确认新增费用并创建独立索引代次。</summary>
    public const string IndexRebuild = "ai.knowledge_documents.index_rebuild";
    /// <summary>所有者使用被冻结的原交互会话恢复失败索引任务。</summary>
    public const string IndexRetry = "ai.knowledge_documents.index_retry";
    /// <summary>读取获授权文档的当前索引状态，不授予上传或派发权限。</summary>
    public const string IndexRead = "ai.knowledge_documents.index_read";
    /// <summary>所有者使用原交互会话启动已获批准的文本索引。</summary>
    public const string IndexEnqueue = "ai.knowledge_documents.index_enqueue";
    /// <summary>上传本人文档的不可变文件版本。</summary>
    public const string Upload = "ai.knowledge_documents.upload";
    /// <summary>读取获授权文档的文件版本元数据。</summary>
    public const string VersionsRead = "ai.knowledge_documents.versions_read";
    /// <summary>下载获授权文档的精确版本来源。</summary>
    public const string SourceRead = "ai.knowledge_documents.source_read";
    /// <summary>所有者读取持久化导入操作。</summary>
    public const string ImportsRead = "ai.knowledge_documents.imports_read";
    /// <summary>所有者恢复导入或重试文件释放。</summary>
    public const string ImportsRetry = "ai.knowledge_documents.imports_retry";
    /// <summary>读取获授权文档元数据，另须知识库读取权及本地成员授权。</summary>
    public const string Read = "ai.knowledge_documents.read";
    /// <summary>所有者创建文档草稿。</summary>
    public const string Create = "ai.knowledge_documents.create";
    /// <summary>所有者编辑文档草稿。</summary>
    public const string Update = "ai.knowledge_documents.update";
    /// <summary>所有者软删除文档并立即禁止读取。</summary>
    public const string Delete = "ai.knowledge_documents.delete";
    /// <summary>所有者读取独立文档授权。</summary>
    public const string MembersRead = "ai.knowledge_documents.members_read";
    /// <summary>所有者授予或撤销独立文档授权。</summary>
    public const string MembersUpdate = "ai.knowledge_documents.members_update";
    /// <summary>所有者读取当前可信租户的活动机构候选，Host 返回空集合。</summary>
    public const string OrganizationCandidatesRead = "ai.knowledge_documents.organization_candidates_read";
    /// <summary>所有者读取启用目录内文档的活动普通角色候选。</summary>
    public const string RoleCandidatesRead = "ai.knowledge_documents.role_candidates_read";
    /// <summary>所有者读取文档角色授予。</summary>
    public const string RolesRead = "ai.knowledge_documents.roles_read";
    /// <summary>所有者替换文档角色授予。</summary>
    public const string RolesUpdate = "ai.knowledge_documents.roles_update";
    /// <summary>所有者读取资源的明确机构授予。</summary>
    public const string OrganizationsRead = "ai.knowledge_documents.organizations_read";
    /// <summary>所有者替换资源的明确机构授予，保留用户与角色授予。</summary>
    public const string OrganizationsUpdate = "ai.knowledge_documents.organizations_update";
}
