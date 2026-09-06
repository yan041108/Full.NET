using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.ImportExport;

/// <summary>向 Identity 授权目录注册 ImportExport 权限、导航与页面操作。</summary>
internal sealed class ImportExportAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("import-export", "导入导出", 115);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(ImportExportPermissions.StaticSchemasRead, "读取静态导入 Schema 目录", AuthorizationScope.Tenant),
        new(ImportExportPermissions.ImportTasksRead, "读取导入任务", AuthorizationScope.Tenant),
        new(ImportExportPermissions.ImportTasksCreate, "创建导入任务并预校验", AuthorizationScope.Tenant),
        new(ImportExportPermissions.ImportTasksExecute, "执行导入任务", AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "import-export-tasks",
            null,
            "import-export-tasks",
            "/import-export/tasks",
            "import-export-tasks",
            "导入任务",
            "Import Tasks",
            "upload",
            10,
            ImportExportPermissions.ImportTasksRead),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "import_export.import_tasks.create",
            "import-export-tasks",
            ImportExportPermissions.ImportTasksCreate,
            "提交导入",
            "create",
            10),
        new AuthorizationActionDefinition(
            "import_export.import_tasks.execute",
            "import-export-tasks",
            ImportExportPermissions.ImportTasksExecute,
            "执行导入",
            "execute",
            20),
    ];
}
