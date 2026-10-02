namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识库目录与文档处理审批的独立精确权限。</summary>
public static class AiKnowledgePermissions
{
    /// <summary>读取本人知识库。</summary>
    public const string Read = "ai.knowledge_bases.read";
    /// <summary>创建私有知识库。</summary>
    public const string Create = "ai.knowledge_bases.create";
    /// <summary>更新本人目录信息。</summary>
    public const string Update = "ai.knowledge_bases.update";
    /// <summary>批准或撤销知识库的精确模型处理范围。</summary>
    public const string PolicyUpdate = "ai.knowledge_bases.policy_update";
}
