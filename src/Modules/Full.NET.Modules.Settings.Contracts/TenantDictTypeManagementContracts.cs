namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// 租户作用域数据字典类型 API 的权限与契约。
/// </summary>
public static class TenantDictTypeManagementPermissions
{
    /// <summary>租户上下文中分页查询字典类型与详情。</summary>
    public const string Read = "settings.tenant_dict_types.read";

    /// <summary>租户上下文中创建、更新与禁用字典类型及字典项。</summary>
    public const string Write = "settings.tenant_dict_types.write";
}
