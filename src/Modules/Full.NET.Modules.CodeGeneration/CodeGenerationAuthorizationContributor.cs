using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.CodeGeneration;

/// <summary>
/// 向统一授权目录贡献 Host 代码生成权限与双管理端导航。
/// </summary>
internal sealed class CodeGenerationAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("code-generation", "代码生成", 80);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            CodeGenerationPreviewPermissions.Read,
            "预览 CRUD 生成产物",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationTemplatePermissions.Read,
            "读取代码生成模板",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationTemplatePermissions.Create,
            "创建代码生成模板",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationTemplatePermissions.Update,
            "更新代码生成模板",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationTemplatePermissions.Delete,
            "删除代码生成模板",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationRunPermissions.Read,
            "读取代码生成运行记录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationRunPermissions.Execute,
            "执行受跟踪代码生成预览",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationRunPermissions.Apply,
            "应用已审查的代码生成预览",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationRunPermissions.Rollback,
            "回滚已成功的代码生成 Apply",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationRunPermissions.Download,
            "下载已成功的代码生成产物",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CodeGenerationCatalogPermissions.Read,
            "读取代码生成数据库目录",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "code-generation-templates",
            null,
            "code-generation-templates",
            "/code-generation/templates",
            "code-generation-templates",
            "代码生成模板",
            "Code Generation Templates",
            "files",
            69,
            CodeGenerationTemplatePermissions.Read),
        new NavigationDefinition(
            "code-generation-previews",
            null,
            "code-generation-previews",
            "/code-generation/previews",
            "code-generation-previews",
            "代码生成预览",
            "Code Generation Preview",
            "code",
            70,
            CodeGenerationPreviewPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "codegen.templates.create",
            "code-generation-templates",
            CodeGenerationTemplatePermissions.Create,
            "创建模板",
            "create",
            10),
        new AuthorizationActionDefinition(
            "codegen.templates.update",
            "code-generation-templates",
            CodeGenerationTemplatePermissions.Update,
            "编辑模板",
            "update",
            20),
        new AuthorizationActionDefinition(
            "codegen.templates.delete",
            "code-generation-templates",
            CodeGenerationTemplatePermissions.Delete,
            "删除模板",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "codegen.catalog.read",
            "code-generation-templates",
            CodeGenerationCatalogPermissions.Read,
            "读取数据库目录",
            "catalog",
            40),
        new AuthorizationActionDefinition(
            "codegen.runs.download",
            "code-generation-previews",
            CodeGenerationRunPermissions.Download,
            "下载生成产物",
            "download",
            10),
    ];
}
