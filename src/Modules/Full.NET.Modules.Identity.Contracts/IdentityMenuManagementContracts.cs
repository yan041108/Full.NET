namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域菜单管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityMenuManagementPermissions
{
    /// <summary>分页查询 Host 菜单列表与详情。</summary>
    public const string Read = "identity.menus.read";

    /// <summary>创建 Host 菜单。</summary>
    public const string Create = "identity.menus.create";

    /// <summary>更新 Host 菜单。</summary>
    public const string Update = "identity.menus.update";

    /// <summary>禁用 Host 菜单。</summary>
    public const string Disable = "identity.menus.disable";

    /// <summary>迁移 057 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "identity.menus.write";
}

/// <summary>创建 Host 菜单请求。</summary>
public sealed record CreateHostMenuRequest(
    string? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    string MenuType = IdentityHostMenuTypes.Menu,
    string? Redirect = null,
    string? LinkUrl = null,
    bool IsHidden = false,
    bool IsKeepAlive = false,
    bool IsAffix = false,
    bool IsEmbedded = false,
    string? Remark = null);

/// <summary>更新 Host 菜单请求。</summary>
public sealed record UpdateHostMenuRequest(
    string? ParentId,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    int Version,
    string MenuType = IdentityHostMenuTypes.Menu,
    string? Redirect = null,
    string? LinkUrl = null,
    bool IsHidden = false,
    bool IsKeepAlive = false,
    bool IsAffix = false,
    bool IsEmbedded = false,
    string? Remark = null);

/// <summary>Host 菜单列表项与详情响应。</summary>
public sealed record HostMenuResponse(
    Guid Id,
    Guid? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version,
    string MenuType,
    string? Redirect,
    string? LinkUrl,
    bool IsHidden,
    bool IsKeepAlive,
    bool IsAffix,
    bool IsEmbedded,
    string? Remark);

/// <summary>Host 菜单可分配权限选项。</summary>
public sealed record HostMenuPermissionOptionResponse(
    string Code,
    string ModuleKey,
    string ModuleTitle,
    string PageId,
    string PageTitle,
    string Kind,
    string DisplayName,
    string DisplayNameKey,
    string? ActionId = null,
    string? ActionKey = null);

/// <summary>将授权目录缺失项同步到 Host 菜单表的结果。</summary>
public sealed record HostNavigationCatalogSyncResponse(
    int Created,
    int Skipped,
    int Reparented);
