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
            CodeGenerationTemplatePermissions.Write,
            "管理代码生成模板",
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
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
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
}