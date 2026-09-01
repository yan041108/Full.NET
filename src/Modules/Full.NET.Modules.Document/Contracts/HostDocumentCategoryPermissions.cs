namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// Host 作用域文档分类管理的稳定权限码目录。
/// </summary>
/// <remarks>
/// 机器码稳定性：发布后所有权限码字符串保持不变，新增只能追加；
/// 授权目录、角色授权与前端路由守卫均按精确匹配校验。
/// </remarks>
public static class HostDocumentCategoryPermissions
{
    /// <summary>分页查询 Host 文档分类树与详情。</summary>
    public const string Read = "document.categories.read";

    /// <summary>创建 Host 文档分类节点。</summary>
    public const string Create = "document.categories.create";

    /// <summary>更新 Host 文档分类名称、父节点或排序。</summary>
    public const string Update = "document.categories.update";

    /// <summary>删除 Host 文档分类；仅允许删除无子分类且未被引用的叶子节点。</summary>
    public const string Delete = "document.categories.delete";
}
