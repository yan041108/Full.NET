using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Calendar;

/// <summary>
/// 向授权目录贡献 Calendar 模块的权限、导航与操作定义。
/// </summary>
internal sealed class CalendarAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    private const AuthorizationScope PlatformScopes =
        AuthorizationScope.Host | AuthorizationScope.Tenant;

    /// <summary>Calendar 模块在授权目录中的定义。</summary>
    public AuthorizationModuleDefinition Module { get; } =
        new("calendar", "日历", 62);

    /// <summary>Calendar 模块全部权限定义。</summary>
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            CalendarPermissions.Read,
            "查询个人日程",
            PlatformScopes),
        new PermissionDefinition(
            CalendarPermissions.Create,
            "创建个人日程",
            PlatformScopes),
        new PermissionDefinition(
            CalendarPermissions.Update,
            "更新个人日程",
            PlatformScopes),
        new PermissionDefinition(
            CalendarPermissions.Delete,
            "删除个人日程",
            PlatformScopes),
        new PermissionDefinition(
            CalendarPermissions.SetStatus,
            "切换个人日程状态",
            PlatformScopes),
    ];

    /// <summary>Calendar 模块导航定义。</summary>
    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "personal-schedules",
            null,
            "personal-schedules",
            "/calendar/personal-schedules",
            "personal-schedules",
            "个人日程",
            "Personal Schedules",
            "calendar",
            63,
            CalendarPermissions.Read),
    ];

    /// <summary>Calendar 模块页面操作定义。</summary>
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "calendar.personal_schedules.create",
            "personal-schedules",
            CalendarPermissions.Create,
            "创建日程",
            "create",
            10),
        new AuthorizationActionDefinition(
            "calendar.personal_schedules.update",
            "personal-schedules",
            CalendarPermissions.Update,
            "编辑日程",
            "update",
            20),
        new AuthorizationActionDefinition(
            "calendar.personal_schedules.delete",
            "personal-schedules",
            CalendarPermissions.Delete,
            "删除日程",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "calendar.personal_schedules.set_status",
            "personal-schedules",
            CalendarPermissions.SetStatus,
            "切换状态",
            "set-status",
            40),
    ];
}
