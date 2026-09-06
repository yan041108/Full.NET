using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting;

/// <summary>向 Identity 授权目录注册 Reporting 权限、导航与页面操作。</summary>
internal sealed class ReportingAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("reporting", "报表", 116);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(ReportingDataSourcePermissions.Read, "读取报表数据源", AuthorizationScope.Host),
        new(ReportingDataSourcePermissions.Create, "创建报表数据源", AuthorizationScope.Host),
        new(ReportingDataSourcePermissions.Update, "更新报表数据源", AuthorizationScope.Host),
        new(ReportingDataSourcePermissions.Delete, "删除报表数据源", AuthorizationScope.Host),
        new(ReportingDataSourcePermissions.Test, "测试报表数据源连接", AuthorizationScope.Host),
        new(ReportingGroupPermissions.Read, "读取报表分组", AuthorizationScope.Host),
        new(ReportingGroupPermissions.Create, "创建报表分组", AuthorizationScope.Host),
        new(ReportingGroupPermissions.Update, "更新报表分组", AuthorizationScope.Host),
        new(ReportingGroupPermissions.Delete, "删除报表分组", AuthorizationScope.Host),
        new(ReportingDefinitionPermissions.Read, "读取报表定义", AuthorizationScope.Host),
        new(ReportingDefinitionPermissions.Create, "创建报表定义", AuthorizationScope.Host),
        new(ReportingDefinitionPermissions.Update, "更新报表定义", AuthorizationScope.Host),
        new(ReportingDefinitionPermissions.Delete, "删除报表定义", AuthorizationScope.Host),
        new(ReportingDefinitionPermissions.Publish, "发布报表定义", AuthorizationScope.Host),
        new(ReportingQueryPortPermissions.Read, "读取静态 Query Port 目录", AuthorizationScope.Host),
        new(ReportingExecutionPermissions.Run, "执行已发布报表", AuthorizationScope.Host),
        new(ReportingExecutionPermissions.ColumnSchemaName, "读取 Schema 清单列", AuthorizationScope.Host),
        new(ReportingExportTaskPermissions.Create, "创建报表导出任务", AuthorizationScope.Host),
        new(ReportingExportTaskPermissions.Read, "读取报表导出任务", AuthorizationScope.Host),
        new(ReportingExportTaskPermissions.Download, "下载报表导出文件", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "reporting-data-sources",
            null,
            "reporting-data-sources",
            "/reporting/data-sources",
            "reporting-data-sources",
            "报表数据源",
            "Reporting Data Sources",
            "data-analysis",
            10,
            ReportingDataSourcePermissions.Read),
        new NavigationDefinition(
            "reporting-definitions",
            null,
            "reporting-definitions",
            "/reporting/definitions",
            "reporting-definitions",
            "报表定义",
            "Reporting Definitions",
            "document",
            20,
            ReportingDefinitionPermissions.Read),
        new NavigationDefinition(
            "reporting-execute",
            null,
            "reporting-execute",
            "/reporting/execute",
            "reporting-execute",
            "报表执行",
            "Reporting Execute",
            "monitor",
            30,
            ReportingExecutionPermissions.Run),
        new NavigationDefinition(
            "reporting-export-tasks",
            null,
            "reporting-export-tasks",
            "/reporting/export-tasks",
            "reporting-export-tasks",
            "报表导出",
            "Reporting Export",
            "download",
            40,
            ReportingExportTaskPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "reporting.data_sources.create",
            "reporting-data-sources",
            ReportingDataSourcePermissions.Create,
            "创建数据源",
            "create",
            10),
        new AuthorizationActionDefinition(
            "reporting.data_sources.update",
            "reporting-data-sources",
            ReportingDataSourcePermissions.Update,
            "编辑数据源",
            "update",
            20),
        new AuthorizationActionDefinition(
            "reporting.data_sources.delete",
            "reporting-data-sources",
            ReportingDataSourcePermissions.Delete,
            "删除数据源",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "reporting.data_sources.test",
            "reporting-data-sources",
            ReportingDataSourcePermissions.Test,
            "测试连接",
            "test",
            40),
        new AuthorizationActionDefinition(
            "reporting.groups.create",
            "reporting-definitions",
            ReportingGroupPermissions.Create,
            "创建分组",
            "create-group",
            10),
        new AuthorizationActionDefinition(
            "reporting.groups.update",
            "reporting-definitions",
            ReportingGroupPermissions.Update,
            "编辑分组",
            "update-group",
            20),
        new AuthorizationActionDefinition(
            "reporting.groups.delete",
            "reporting-definitions",
            ReportingGroupPermissions.Delete,
            "删除分组",
            "delete-group",
            30),
        new AuthorizationActionDefinition(
            "reporting.definitions.create",
            "reporting-definitions",
            ReportingDefinitionPermissions.Create,
            "创建定义",
            "create-definition",
            40),
        new AuthorizationActionDefinition(
            "reporting.definitions.update",
            "reporting-definitions",
            ReportingDefinitionPermissions.Update,
            "编辑定义",
            "update-definition",
            50),
        new AuthorizationActionDefinition(
            "reporting.definitions.delete",
            "reporting-definitions",
            ReportingDefinitionPermissions.Delete,
            "删除定义",
            "delete-definition",
            60),
        new AuthorizationActionDefinition(
            "reporting.definitions.publish",
            "reporting-definitions",
            ReportingDefinitionPermissions.Publish,
            "发布版本",
            "publish",
            70),
    ];
}
