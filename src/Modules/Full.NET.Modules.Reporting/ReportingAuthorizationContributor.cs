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
    ];
}
