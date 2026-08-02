using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs;

internal sealed class JobsAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("jobs", "任务调度", 70);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            HostJobPermissions.DefinitionsRead,
            "查询任务定义",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.DefinitionsCreate,
            "创建任务定义",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.DefinitionsUpdate,
            "更新任务定义",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.DefinitionsDisable,
            "禁用任务定义",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.DefinitionsTrigger,
            "手动触发任务",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.ExecutionsRead,
            "查询任务执行记录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesRead,
            "查询任务计划",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesWrite,
            "管理任务计划",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-jobs",
            null,
            "host-jobs",
            "/jobs/host-definitions",
            "host-jobs",
            "任务调度",
            "Jobs",
            "timer",
            57,
            HostJobPermissions.DefinitionsRead),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "jobs.definitions.create",
            "host-jobs",
            HostJobPermissions.DefinitionsCreate,
            "创建任务定义",
            "create",
            10),
        new AuthorizationActionDefinition(
            "jobs.definitions.update",
            "host-jobs",
            HostJobPermissions.DefinitionsUpdate,
            "编辑任务定义",
            "update",
            20),
        new AuthorizationActionDefinition(
            "jobs.definitions.disable",
            "host-jobs",
            HostJobPermissions.DefinitionsDisable,
            "禁用任务定义",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "jobs.definitions.trigger",
            "host-jobs",
            HostJobPermissions.DefinitionsTrigger,
            "手动触发任务",
            "trigger",
            40),
    ];
}
