namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// 租户作用域数据字典类型 API 的权限与契约。
/// </summary>
public static class TenantDictTypeManagementPermissions
{
    /// <summary>租户上下文中分页查询字典类型与详情。</summary>
    public const string Read = "settings.tenant_dict_types.read";

    /// <summary>分配租户字典类型与字典项。</summary>
    public const string Create = "settings.tenant_dict_types.create";

    /// <summary>更新租户字典类型与字典项。</summary>
    public const string Update = "settings.tenant_dict_types.update";

    /// <summary>禁用租户字典类型与字典项。</summary>
    public const string Disable = "settings.tenant_dict_types.disable";

    /// <summary>硬删除已禁用且无活跃字典项的租户字典类型；字典项删除复用该权限。</summary>
    public const string Delete = "settings.tenant_dict_types.delete";

    /// <summary>迁移 068 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "settings.tenant_dict_types.write";
}
