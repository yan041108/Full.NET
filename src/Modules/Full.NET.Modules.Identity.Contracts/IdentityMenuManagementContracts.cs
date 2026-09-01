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
/// <param name="ParentId">父菜单稳定标识；<see langword="null"/> 表示创建根节点。</param>
/// <param name="RouteName">客户端路由名称；在菜单表内唯一。</param>
/// <param name="Path">客户端路由路径。</param>
/// <param name="ComponentKey">客户端本地组件白名单键。</param>
/// <param name="Title">菜单标题。</param>
/// <param name="Caption">辅助说明。</param>
/// <param name="Icon">客户端图标语义键。</param>
/// <param name="DisplayOrder">同级稳定排序值。</param>
/// <param name="RequiredPermission">显示该菜单所需的权限码。</param>
/// <param name="MenuType">菜单类型，参见 <see cref="IdentityHostMenuTypes"/>。</param>
/// <param name="Redirect">可选重定向路径。</param>
/// <param name="LinkUrl">可选外链或内嵌地址。</param>
/// <param name="IsHidden">是否在侧栏隐藏。</param>
/// <param name="IsKeepAlive">是否缓存页面实例。</param>
/// <param name="IsAffix">是否固定在标签栏。</param>
/// <param name="IsEmbedded">是否以内嵌方式打开。</param>
/// <param name="Remark">备注。</param>
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
/// <param name="ParentId">更新后的父菜单标识；<see langword="null"/> 表示根节点。</param>
/// <param name="Path">客户端路由路径。</param>
/// <param name="ComponentKey">客户端本地组件白名单键。</param>
/// <param name="Title">菜单标题。</param>
/// <param name="Caption">辅助说明。</param>
/// <param name="Icon">客户端图标语义键。</param>
/// <param name="DisplayOrder">同级稳定排序值。</param>
/// <param name="RequiredPermission">显示该菜单所需的权限码。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
/// <param name="MenuType">菜单类型，参见 <see cref="IdentityHostMenuTypes"/>。</param>
/// <param name="Redirect">可选重定向路径。</param>
/// <param name="LinkUrl">可选外链或内嵌地址。</param>
/// <param name="IsHidden">是否在侧栏隐藏。</param>
/// <param name="IsKeepAlive">是否缓存页面实例。</param>
/// <param name="IsAffix">是否固定在标签栏。</param>
/// <param name="IsEmbedded">是否以内嵌方式打开。</param>
/// <param name="Remark">备注。</param>
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
/// <param name="Id">菜单稳定标识。</param>
/// <param name="ParentId">父菜单标识；根节点时为 <see langword="null"/>。</param>
/// <param name="RouteName">客户端路由名称。</param>
/// <param name="Path">客户端路由路径。</param>
/// <param name="ComponentKey">客户端本地组件白名单键。</param>
/// <param name="Title">菜单标题。</param>
/// <param name="Caption">辅助说明。</param>
/// <param name="Icon">客户端图标语义键。</param>
/// <param name="DisplayOrder">同级稳定排序值。</param>
/// <param name="RequiredPermission">显示该菜单所需的权限码。</param>
/// <param name="IsSystem">是否为系统受保护菜单；系统菜单禁止编辑或删除。</param>
/// <param name="IsActive">是否处于活动状态；禁用菜单不再进入导航。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近一次更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本。</param>
/// <param name="MenuType">菜单类型，参见 <see cref="IdentityHostMenuTypes"/>。</param>
/// <param name="Redirect">可选重定向路径。</param>
/// <param name="LinkUrl">可选外链或内嵌地址。</param>
/// <param name="IsHidden">是否在侧栏隐藏。</param>
/// <param name="IsKeepAlive">是否缓存页面实例。</param>
/// <param name="IsAffix">是否固定在标签栏。</param>
/// <param name="IsEmbedded">是否以内嵌方式打开。</param>
/// <param name="Remark">备注。</param>
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
/// <param name="Code">稳定权限码。</param>
/// <param name="ModuleKey">所属模块稳定键。</param>
/// <param name="ModuleTitle">所属模块中文标题。</param>
/// <param name="PageId">所属页面稳定标识。</param>
/// <param name="PageTitle">所属页面中文标题。</param>
/// <param name="Kind">权限种类：页面或操作。</param>
/// <param name="DisplayName">面向管理员展示的中文名称。</param>
/// <param name="DisplayNameKey">可被多语言资源解析的展示名键。</param>
/// <param name="ActionId">操作目录标识；仅当 Kind 为操作时存在。</param>
/// <param name="ActionKey">客户端本地操作白名单键；仅当 Kind 为操作时存在。</param>
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
/// <param name="Created">本次新增的菜单节点数量。</param>
/// <param name="Skipped">已存在且保持不变的菜单节点数量。</param>
/// <param name="Reparented">因目录结构调整而重新挂接父节点的菜单数量。</param>
public sealed record HostNavigationCatalogSyncResponse(
    int Created,
    int Skipped,
    int Reparented);
