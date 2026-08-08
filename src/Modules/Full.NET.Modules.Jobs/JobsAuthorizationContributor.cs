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
            HostJobPermissions.DefinitionsDelete,
            "删除任务定义",
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
            HostJobPermissions.ExecutionsClear,
            "清空任务执行记录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesRead,
            "查询任务计划",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesCreate,
            "创建任务计划",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesUpdate,
            "更新任务计划",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesDelete,
            "删除任务计划",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesPause,
            "暂停任务计划",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostJobPermissions.SchedulesResume,
            "恢复任务计划",
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
            "任务定义",
            "Jobs",
            "timer",
            57,
            HostJobPermissions.DefinitionsRead),
        new NavigationDefinition(
            "host-job-schedules",
            null,
            "host-job-schedules",
            "/jobs/host-schedules",
            "host-job-schedules",
            "任务计划",
            "Jobs",
            "calendar",
            58,
            HostJobPermissions.SchedulesRead),
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
            "jobs.definitions.delete",
            "host-jobs",
            HostJobPermissions.DefinitionsDelete,
            "删除任务定义",
            "delete",
            35),
        new AuthorizationActionDefinition(
            "jobs.definitions.trigger",
            "host-jobs",
            HostJobPermissions.DefinitionsTrigger,
            "手动触发任务",
            "trigger",
            40),
        new AuthorizationActionDefinition(
            "jobs.schedules.create",
            "host-job-schedules",
            HostJobPermissions.SchedulesCreate,
            "创建任务计划",
            "create",
            10),
        new AuthorizationActionDefinition(
            "jobs.schedules.update",
            "host-job-schedules",
            HostJobPermissions.SchedulesUpdate,
            "编辑任务计划",
            "update",
            20),
        new AuthorizationActionDefinition(
            "jobs.schedules.delete",
            "host-job-schedules",
            HostJobPermissions.SchedulesDelete,
            "删除任务计划",
            "delete",
            25),
        new AuthorizationActionDefinition(
            "jobs.schedules.pause",
            "host-job-schedules",
            HostJobPermissions.SchedulesPause,
            "暂停任务计划",
            "pause",
            30),
        new AuthorizationActionDefinition(
            "jobs.schedules.resume",
            "host-job-schedules",
            HostJobPermissions.SchedulesResume,
            "恢复任务计划",
            "resume",
            40),
        new AuthorizationActionDefinition(
            "jobs.executions.clear",
            "host-jobs",
            HostJobPermissions.ExecutionsClear,
            "清空执行记录",
            "clear",
            50),
    ];
}
