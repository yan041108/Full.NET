namespace Full.NET.Modules.Ai.Contracts;

/// <summary>文档管理精确权限；目录读取权不自动授予文档读取。</summary>
public static class AiKnowledgeDocumentPermissions
{
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
}
