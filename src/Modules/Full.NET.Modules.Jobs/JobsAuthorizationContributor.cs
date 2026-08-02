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
            HostJobPermissions.DefinitionsWrite,
            "管理任务定义",
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
}
