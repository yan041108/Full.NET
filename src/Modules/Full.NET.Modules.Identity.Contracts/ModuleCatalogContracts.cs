namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Host 只读模块清单权限。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class ModuleCatalogPermissions
{
    /// <summary>查询官方模块清单与详情。</summary>
    public const string Read = "identity.modules.read";
}

/// <summary>模块清单列表项；不包含源码、程序集或加载路径。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 ModuleKey 发布后不可改名。</remarks>
/// <param name="ModuleKey">稳定模块键，供前后端和授权目录引用。</param>
/// <param name="DisplayName">面向管理端展示的模块名称。</param>
/// <param name="Version">当前模块版本摘要。</param>
/// <param name="Dependencies">模块声明的上游依赖集合。</param>
/// <param name="HostProfiles">当前模块允许装配的宿主角色集合。</param>
/// <param name="SourceClassification">模块来源分类，用于区分官方、兼容或其他受控来源。</param>
/// <param name="HealthCapability">模块声明的健康能力摘要。</param>
public sealed record ModuleCatalogEntryResponse(
    string ModuleKey,
    string DisplayName,
    string Version,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> HostProfiles,
    string SourceClassification,
    string HealthCapability);
