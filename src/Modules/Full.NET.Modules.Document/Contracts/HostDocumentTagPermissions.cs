namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// Host 作用域文档标签管理的稳定权限码目录。
/// </summary>
/// <remarks>
/// 机器码稳定性：发布后所有权限码字符串保持不变，新增只能追加；
/// 授权目录、角色授权与前端路由守卫均按精确匹配校验。
/// </remarks>
public static class HostDocumentTagPermissions
{
    /// <summary>分页查询 Host 文档标签列表与详情。</summary>
    public const string Read = "document.tags.read";

    /// <summary>创建 Host 文档标签。</summary>
    public const string Create = "document.tags.create";

    /// <summary>更新 Host 文档标签名称或颜色。</summary>
    public const string Update = "document.tags.update";

    /// <summary>删除 Host 文档标签；仅允许删除未被引用的标签。</summary>
    public const string Delete = "document.tags.delete";
}
