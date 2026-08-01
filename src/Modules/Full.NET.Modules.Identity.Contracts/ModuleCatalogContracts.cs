namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Host 只读模块清单权限。</summary>
public static class ModuleCatalogPermissions
{
    /// <summary>查询官方模块清单与详情。</summary>
    public const string Read = "identity.modules.read";
}

/// <summary>模块清单列表项；不包含源码、程序集或加载路径。</summary>
public sealed record ModuleCatalogEntryResponse(
    string ModuleKey,
    string DisplayName,
    string Version,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> HostProfiles,
    string SourceClassification,
    string HealthCapability);
